using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Text.Json;
using Constant_Classes;

namespace DosGame_Server
{
    class WebServer
    {
        private static readonly byte MAX_NUM_PLAYERS = 4;

        private Dictionary<int, TcpClient> _playerConnections;
        private Dictionary<int, string> _playerNames;
        private int _currId;
        private bool _inGame;
        private GameEngine? _gameEngine;

        public WebServer()
        {
            _playerConnections = new Dictionary<int, TcpClient>();
            _currId = -1;
            _playerNames = new Dictionary<int, string>();
            _inGame = false;
        }

        /// <summary>
        /// Starts listening to messages
        /// sent by clients.
        /// </summary>
        public void Start()
        {
            TcpListener serverSocket = new TcpListener(IPAddress.Any, 8888);
            serverSocket.Start();
            Console.WriteLine("Server has started listening");

            try
            {
                while (true)
                {
                    TcpClient clientSocket = serverSocket.AcceptTcpClient();
                    StartClientSession(clientSocket);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                serverSocket.Stop();
                Console.WriteLine("Server has stopped listening");
            }
        }

        #region RequestHandlers

        /// <summary>
        /// Creates a new task that runs
        /// in the background to handle
        /// any user request.
        /// </summary>
        /// <param name="clientSocket"></param>
        private async void StartClientSession(TcpClient clientSocket)
        {
            int userId = GetNextUserId();
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            CancellationToken token = tokenSource.Token;
            try
            {
                await Task.Run(() =>
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        RespondToUserRequest(clientSocket, tokenSource, userId);
                        if (token.IsCancellationRequested)
                        {
                            token.ThrowIfCancellationRequested();
                        }
                    }
                }, tokenSource.Token);
            }
            catch (Exception)
            {
                CleanUpAfterDisconnect(clientSocket, userId);
            }
        }

        /// <summary>
        /// Executes a specific action depending
        /// on what kind of command the server receives
        /// from the client.
        /// </summary>
        /// <param name="clientSocket"></param>
        /// <param name="cancellationTokenSrc"></param>
        /// <param name="userId"></param>
        private void RespondToUserRequest(TcpClient clientSocket, CancellationTokenSource cancellationTokenSrc, int userId)
        {
            try
            {
                string data = ReadDataFromUser(clientSocket);
                Protocol? protocol = JsonSerializer.Deserialize<Protocol>(data);
                if (protocol != null)
                {
                    switch (protocol.Command)
                    {
                        case Protocol.Commands.JOIN_QUEUE:
                            HandleJoinQueueRequest(clientSocket, userId, cancellationTokenSrc);
                            break;
                        case Protocol.Commands.DISCONNECT:
                            cancellationTokenSrc.Cancel();
                            break;
                        case Protocol.Commands.START_GAME:
                            HandleStartGameRequest(clientSocket);
                            break;
                        case Protocol.Commands.CARD_SUBMISSION:
                            HandleCardSubmission(clientSocket, userId, protocol.Data);
                            break;
                        case Protocol.Commands.END_TURN:
                            HandleEndTurnRequest(clientSocket, userId);
                            break;
                        case Protocol.Commands.ROW_CARD_ADDITION:
                            HandleRowCardAdditionRequest(clientSocket, userId, protocol.Data);
                            break;
                        case Protocol.Commands.DRAW_CARD:
                            HandleDrawCardRequest(clientSocket, userId);
                            break;
                        case Protocol.Commands.CALL_DOS:
                            HandleDosCall(clientSocket, userId, protocol.Data);
                            break;
                        default:
                            HandleUnknownCommand(clientSocket);
                            cancellationTokenSrc.Cancel();
                            break;
                    }
                }
                else
                {
                    string errorMessage = JsonSerializer.Serialize(GetErrorProtocol("Invalid protocol formating."));
                    WriteResponseToUser(errorMessage, clientSocket);
                }
            }
            catch (JsonException)
            {
                string errorMessage = JsonSerializer.Serialize(GetErrorProtocol("Invalid protocol formating."));
                WriteResponseToUser(errorMessage, clientSocket);
            }
        }

        /// <summary>
        /// Gets called whenever a client requests
        /// a Dos Call command type. Notifies all
        /// clients in the lobby of the changes
        /// made in the game engine if no
        /// errors are found. Otherwise,
        /// an error message is sent back
        /// to the sender.
        /// </summary>
        /// <param name="clientSocket"></param>
        /// <param name="userId"></param>
        /// <param name="data"></param>
        private void HandleDosCall(TcpClient clientSocket, int userId, Dictionary<string, string>? data)
        {
            string errorMessage = "";
            if (!_inGame || _gameEngine == null)
            {
                errorMessage = "Game has not started yet.";
            }
            else
            {
                if (data != null)
                {
                    if (!data.ContainsKey("PlayerToPunish"))
                    {
                        errorMessage = "Data given does not contain expected value.";
                    }
                    else
                    {
                        int playerIdToPunish = GetPlayerIdFromName(data["PlayerToPunish"]);
                        if (playerIdToPunish == -1)
                        {
                            errorMessage = "Player to punish given does not exists.";
                        }
                        else
                        {
                            errorMessage = _gameEngine.CallDos(userId, playerIdToPunish);
                        }
                    }
                }
                else
                {
                    errorMessage = _gameEngine.CallDos(userId);
                }
            }

            if (errorMessage != "")
            {
                WriteResponseToUser(JsonSerializer.Serialize(GetErrorProtocol(errorMessage)), clientSocket);
            }
            else
            {
                NotifyGameStateChange();
            }
        }

        /// <summary>
        /// Is called whenever a client
        /// sends a Draw card command type.
        /// Notifies all clients in the lobby
        /// for changes in the game engine only
        /// when no errors are found. Otherwise,
        /// sends an error message back to the sender.
        /// </summary>
        /// <param name="clientSocket"></param>
        /// <param name="userId"></param>
        private void HandleDrawCardRequest(TcpClient clientSocket, int userId)
        {
            string errorMessage = "";
            if (_gameEngine != null && _inGame)
            {
                errorMessage = _gameEngine.DrawCard(userId);
            }
            else
            {
                errorMessage = "There is no game in progress.";
            }

            if (errorMessage != "")
            {
                WriteResponseToUser(JsonSerializer.Serialize(GetErrorProtocol(errorMessage)), clientSocket);
            }
            else
            {
                NotifyGameStateChange();
            }
        }

        /// <summary>
        /// Gets called whenever a client
        /// requests a row card addition
        /// command type. Notifies all
        /// clients in the lobby if
        /// a change happen in the
        /// game engine and if there
        /// was no errors. Otherwise,
        /// the server responds with
        /// an error message back to the
        /// client.
        /// </summary>
        /// <param name="clientSocket"></param>
        /// <param name="userId"></param>
        /// <param name="data"></param>
        private void HandleRowCardAdditionRequest(TcpClient clientSocket, int userId, Dictionary<string, string>? data)
        {
            string errorMessage = "";
            if (!_inGame || _gameEngine == null)
            {
                errorMessage = "There is no game in progress.";
            }
            else if (data == null || !data.ContainsKey("RowCardToAdd"))
            {
                errorMessage = "Invalid data sent";
            }
            else
            {
                try
                {
                    DosCard? cardToAdd = JsonSerializer.Deserialize<DosCard>(data["RowCardToAdd"]);
                    if (cardToAdd != null)
                    {
                        errorMessage = _gameEngine.AddCardToRow(cardToAdd, userId);
                    }
                    else
                    {
                        errorMessage = "The value of the card sent is null";
                    }
                }
                catch (Exception)
                {
                    errorMessage = "Invalid data sent";
                }
            }

            if (errorMessage != "")
            {
                WriteResponseToUser(JsonSerializer.Serialize(GetErrorProtocol(errorMessage)), clientSocket);
            }
            else
            {
                NotifyGameStateChange();
            }
        }

        /// <summary>
        /// Gets called whenever an end
        /// turn command type is received.
        /// Notifies all clients in the lobby
        /// if a change happened in the game engine.
        /// If an error is found, an error is sent
        /// back to the sender.
        /// </summary>
        /// <param name="clientSocket"></param>
        /// <param name="userId"></param>
        private void HandleEndTurnRequest(TcpClient clientSocket, int userId)
        {
            string errorMessage = "";
            if (_gameEngine != null && _inGame)
            {
                errorMessage = _gameEngine.EndPlayerTurn(userId);
            }
            else
            {
                errorMessage = "There is no game in progress.";
            }

            if (errorMessage != "")
            {
                WriteResponseToUser(JsonSerializer.Serialize(GetErrorProtocol(errorMessage)), clientSocket);
            }
            else
            {
                NotifyGameStateChange();
            }
        }

        /// <summary>
        /// Gets called whenever a
        /// client requests a card
        /// submission command type.
        /// Notifies all clients in the
        /// lobby if a change happened
        /// in the game engine. If an
        /// error is found, notifies
        /// only the sender.
        /// </summary>
        /// <param name="clientSocket"></param>
        /// <param name="userId"></param>
        /// <param name="data"></param>
        private void HandleCardSubmission(TcpClient clientSocket, int userId, Dictionary<string, string>? data)
        {
            string errorMessage = "";
            if (!_inGame || _gameEngine == null)
            {
                errorMessage = JsonSerializer.Serialize(GetErrorProtocol("Game has not started yet."));
            }
            else if (data == null)
            {
                errorMessage = JsonSerializer.Serialize(GetErrorProtocol("Data sent is null."));
            }
            else if (!data.ContainsKey("CardsSubmitted") || !data.ContainsKey("RowCardToPlay"))
            {
                errorMessage = JsonSerializer.Serialize(GetErrorProtocol("Cards submitted missing."));
            }
            else
            {
                try
                {
                    List<DosCard>? userCards = JsonSerializer.Deserialize<List<DosCard>>(data["CardsSubmitted"]);
                    DosCard? rowCard = JsonSerializer.Deserialize<DosCard>(data["RowCardToPlay"]);
                    if (userCards != null && rowCard != null)
                    {
                        errorMessage = _gameEngine.PlayCards(userCards, rowCard, userId);
                    }
                    else
                    {
                        errorMessage = JsonSerializer.Serialize(GetErrorProtocol("No list of Dos cards sent"));
                    }
                }
                catch (JsonException)
                {
                    errorMessage = JsonSerializer.Serialize(GetErrorProtocol("Invalid protocol formating."));
                }
            }

            if (errorMessage != "")
            {
                string errorProtocol = JsonSerializer.Serialize(GetErrorProtocol(errorMessage));
                WriteResponseToUser(errorProtocol, clientSocket);
            }
            else
            {
                NotifyGameStateChange();
            }
        }

        /// <summary>
        /// Gets called whenever the command
        /// requested does not exists.
        /// Sends the response back to the
        /// sender.
        /// </summary>
        /// <param name="clientSocket"></param>
        private void HandleUnknownCommand(TcpClient clientSocket)
        {
            string errorMessage = JsonSerializer.Serialize(GetErrorProtocol("Unknown command provided"));
            WriteResponseToUser(errorMessage, clientSocket);
        }

        /// <summary>
        /// Gets called whenever a
        /// client requests a join queue
        /// command type. If no error
        /// is found, a unique player name is
        /// assigned to the client and
        /// its connection is saved in
        /// the attribute _playerConnections.
        /// </summary>
        /// <param name="clientSocket"></param>
        /// <param name="userId"></param>
        /// <param name="cancellationTokenSource"></param>
        private void HandleJoinQueueRequest(TcpClient clientSocket, int userId, CancellationTokenSource cancellationTokenSource)
        {
            Protocol responseToNewPlayerProtocol;
            string jsonResponse;

            bool hadErrors = false;
            if (!_inGame)
            {
                if (_playerConnections.Count < MAX_NUM_PLAYERS)
                {
                    if (!_playerConnections.ContainsKey(userId) && !_playerNames.ContainsKey(userId))
                    {
                        string newPlayerName = $"Player {userId + 1}";
                        _playerNames.Add(userId, newPlayerName);
                        NotifyLobbyChange();

                        responseToNewPlayerProtocol = new Protocol
                        {
                            Command = Protocol.Commands.Ok,
                            Data = new Dictionary<string, string>
                            {
                                { "AssignedPlayerName", newPlayerName },
                                { "CurrentPlayersInLobby", GetPlayerNamesFromMap() }
                            }
                        };
                        _playerConnections.Add(userId, clientSocket);
                    }
                    else
                    {
                        hadErrors = true;
                        responseToNewPlayerProtocol = GetErrorProtocol("Cannot rejoin lobby.");
                    }
                }
                else
                {
                    hadErrors = true;
                    responseToNewPlayerProtocol = GetErrorProtocol("Lobby has reached maximum capacity.");
                }
            }
            else
            {
                hadErrors = true;
                responseToNewPlayerProtocol = GetErrorProtocol("Cannot join a game in progress");
            }
            jsonResponse = JsonSerializer.Serialize(responseToNewPlayerProtocol);
            WriteResponseToUser(jsonResponse, clientSocket);
            if (hadErrors)
            {
                cancellationTokenSource.Cancel();
            }
        }

        /// <summary>
        /// Gets called whenever a client requests
        /// a start game command type. If no
        /// error is found, the server switches
        /// to _inGame mode and initializes
        /// a game engine to start the
        /// game. Notifies all the clients in
        /// the lobby that the game started.
        /// </summary>
        /// <param name="clientSocket"></param>
        private void HandleStartGameRequest(TcpClient clientSocket)
        {
            if (_playerConnections.Count < 2 || _playerNames.Count < 2)
            {
                string errorMessage = JsonSerializer.Serialize(GetErrorProtocol("Not enough players to start game."));
                WriteResponseToUser(errorMessage, clientSocket);
            }
            else if (_inGame)
            {
                string errorMessage = JsonSerializer.Serialize(GetErrorProtocol("Game already started."));
                WriteResponseToUser(errorMessage, clientSocket);
            }
            else
            {
                _gameEngine = new GameEngine(GetIdsFromMap());
                _inGame = true;
                NotifyGameStateChange();
            }
        }

        #endregion

        #region HelperFunctions

        /// <summary>
        /// Transmits important
        /// information about the state
        /// of the game to every player
        /// that is currently in the lobby.
        /// This includes the cards that each
        /// player has, who won the round,
        /// the number of points that each player
        /// has, who's turn it is, the number
        /// of bonuses that the current player
        /// has, etc.
        /// </summary>
        private void NotifyGameStateChange()
        {
            if (_gameEngine != null)
            {
                string winnerPlayerName = "";
                string playerScores = "";
                bool roundWon = false;

                if (_gameEngine.PlayerIdWinner != -1)
                {
                    winnerPlayerName = _playerNames[_gameEngine.PlayerIdWinner];
                    playerScores = JsonSerializer.Serialize(GetPlayerNameScores(_gameEngine.PlayerScores));
                    roundWon = true;
                    _gameEngine.RestartRound();
                }
                foreach (var player in _playerConnections)
                {
                    if (!_gameEngine.GameFinished)
                    {
                        Protocol protocol = new Protocol()
                        {
                            Command = Protocol.Commands.GAME_UPDATE,
                            Data = new Dictionary<string, string>()
                            {
                                { "CurrPlayerTurnName", _playerNames[_gameEngine.CurrPlayerId] },
                                { "CurrCards", JsonSerializer.Serialize(_gameEngine.CardsPerPlayer[player.Key]) },
                                { "RowCards", JsonSerializer.Serialize(_gameEngine.RowCards) },
                                { "NumCardsPerPlayer", JsonSerializer.Serialize(_gameEngine.GetNumCardsPerPlayer(_playerNames)) },
                                { "CurrPlayerBonus", _gameEngine.CurrPlayerBonus.ToString() },
                            }
                        };
                        if (_gameEngine.PlayerIdPenalizedByDosCall != -1)
                        {
                            protocol.Data.Add("PlayerPenalizedByDosCall", _playerNames[_gameEngine.PlayerIdPenalizedByDosCall]);
                        }
                        if (roundWon)
                        {
                            protocol.Data.Add("RoundWinner", winnerPlayerName);
                            protocol.Data.Add("PlayerScores", playerScores);
                        }
                        string message = JsonSerializer.Serialize(protocol);
                        WriteResponseToUser(message, player.Value);
                    }
                    else
                    {
                        Protocol protocol = new Protocol()
                        {
                            Command = Protocol.Commands.GAME_FINISHED,
                            Data = new Dictionary<string, string>()
                            {
                                { "PlayerWinner", winnerPlayerName},
                            }
                        };
                        string message = JsonSerializer.Serialize(protocol);
                        WriteResponseToUser(message, player.Value);
                    }
                }
                if (_gameEngine.GameFinished)
                {
                    TerminateGame();
                }
            }
        }

        /// <summary>
        /// Cleans up anything related
        /// the given player id. This
        /// includes its player name,
        /// connection, and any data
        /// contained in the game engine.
        /// Usually called whenever a client
        /// exits the lobby.
        /// </summary>
        /// <param name="clientSocket"></param>
        /// <param name="playerId"></param>
        private void CleanUpAfterDisconnect(TcpClient clientSocket, int playerId)
        {
            if (_gameEngine != null && _playerConnections.ContainsKey(playerId))
            {
                _gameEngine.RemovePlayer(playerId);
            }
            _playerConnections.Remove(playerId);
            _playerNames.Remove(playerId);
            if (clientSocket.Connected)
            {
                clientSocket.Close();
            }
            NotifyLobbyChange();
            if (_gameEngine != null)
            {
                NotifyGameStateChange();
            }
        }

        /// <summary>
        /// Notifies all clients
        /// in the lobby if there was
        /// a change in the lobby.
        /// </summary>
        private void NotifyLobbyChange()
        {
            foreach (var connection in _playerConnections)
            {
                Protocol newPlayersInLobbyProtocol = new Protocol
                {
                    Command = Protocol.Commands.LOBBY_UPDATE,
                    Data = new Dictionary<string, string>
                    {
                        { "CurrentPlayersInLobby", GetPlayerNamesFromMap() },
                    }
                };
                string message = JsonSerializer.Serialize(newPlayersInLobbyProtocol);
                WriteResponseToUser(message, connection.Value);
            }
        }

        /// <summary>
        /// Kicks out all players
        /// that were in the lobby
        /// and changes the mode of
        /// the server to not be _inGame.
        /// </summary>
        private void TerminateGame()
        {
            _inGame = false;
            _gameEngine = null;
            _playerNames.Clear();
            foreach (var connection in _playerConnections)
            {
                connection.Value.Close();
            }
            _playerConnections.Clear();
        }
        
        /// <summary>
        /// Returns an error protocol
        /// with the given message.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        private Protocol GetErrorProtocol(string message)
        {
            return new Protocol
            {
                Command = Protocol.Commands.INVALID_REQUEST,
                Data = new Dictionary<string, string>
                {
                    { "ErrorMessage", message }
                }
            };
        }

        /// <summary>
        /// Returns a JSON string
        /// representing the array of
        /// players that are currently
        /// in the lobby.
        /// </summary>
        /// <returns></returns>
        private string GetPlayerNamesFromMap()
        {
            List<string> playerList = new List<string>();
            foreach (var player in _playerNames)
            {
                playerList.Add(player.Value);
            }
            return JsonSerializer.Serialize(playerList);
        }

        /// <summary>
        /// Returns a list of player ids
        /// that are currently in the lobby.
        /// </summary>
        /// <returns></returns>
        private List<int> GetIdsFromMap()
        {
            List<int> ids = new List<int>();
            foreach (var player in _playerNames)
            {
                ids.Add(player.Key);
            }
            return ids;
        }

        /// <summary>
        /// Returns the first player id
        /// that matches the given player name.
        /// Since the player name is unique,
        /// it should always return the only
        /// player id in the _playerNames map.
        /// If player name not found, the method
        /// returns -1.
        /// </summary>
        /// <param name="playerName"></param>
        /// <returns></returns>
        private int GetPlayerIdFromName(string playerName)
        {
            foreach (var player in _playerNames)
            {
                if (player.Value == playerName)
                {
                    return player.Key;
                }
            }
            return -1;
        }

        /// <summary>
        /// Returns the next unique id.
        /// Increments _currId after
        /// calling this method.
        /// </summary>
        /// <returns></returns>
        private int GetNextUserId()
        {
            return ++_currId;
        }

        /// <summary>
        /// Returns a dictionary
        /// that contains each player's
        /// name along with their respective
        /// score.
        /// </summary>
        /// <param name="playerScores"></param>
        /// <returns></returns>
        private Dictionary<string, int> GetPlayerNameScores(Dictionary<int, int> playerScores)
        {
            Dictionary<string, int> playerNameScores = new Dictionary<string, int>();
            foreach (var player in playerScores)
            {
                playerNameScores.Add(_playerNames[player.Key], player.Value);
            }
            return playerNameScores;
        }

        /// <summary>
        /// Sends given message to
        /// the given client.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="client"></param>
        private void WriteResponseToUser(string message, TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] bytes = Encoding.UTF8.GetBytes(message);

            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        /// <summary>
        /// Returns a string representing
        /// the message sent by the client.
        /// </summary>
        /// <param name="clientSocket"></param>
        /// <returns></returns>
        private string ReadDataFromUser(TcpClient clientSocket)
        {
            byte[] bytes = new byte[16384];
            NetworkStream stream = clientSocket.GetStream();

            stream.Read(bytes, 0, bytes.Length);

            string data = Encoding.UTF8.GetString(bytes);
            data = data.Replace("\0", "");

            return data;
        }

        #endregion
    }
}
