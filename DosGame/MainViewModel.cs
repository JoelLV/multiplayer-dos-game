using Constant_Classes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DosGame_UI
{
    internal class MainViewModel : INotifyPropertyChanged
    {
        private bool _inGame;
        private bool _allowJoinQueue;
        private bool _allowLeaveQueue;
        private bool _allowStartGame;
        private string _gameStatus;
        private string _playerName;
        private string _cardRowSelected;
        private string _playerNameSelected;
        private Dictionary<string, int> _numOfCardsPerPlayer;
        private CancellationTokenSource _serverListenerTokenSrc;
        private ClientModel _clientModel;
        private BindingList<string> _playersInLobby;
        private BindingList<string> _playerCards;
        private BindingList<string> _cardRows;
        private List<DosCard> _selectedUserCards;

        #region Properties

        public event PropertyChangedEventHandler? PropertyChanged;

        public DelegateCommand JoinQueue { get; set; }

        public DelegateCommand LeaveQueue { get; set; }

        public DelegateCommand StartGame { get; set; }

        public DelegateCommand ChangeUserCardSelectionCommand { get; set; }

        public DelegateCommand SendCards { get; set; }

        public DelegateCommand EndTurn { get; set; }

        public DelegateCommand AddRowCard { get; set; }

        public DelegateCommand WithdrawCard { get; set; }

        public DelegateCommand CallDos { get; set; }

        public bool InGame
        {
            get => _inGame;
            set
            {
                _inGame = value;
                NotifyPropertyChanged();
            }
        }

        public bool AllowStartGame
        {
            get => _allowStartGame;
            set
            {
                _allowStartGame = value;
                NotifyPropertyChanged();
            }
        }

        public bool AllowJoinQueue 
        { 
            get => _allowJoinQueue;
            set
            {
                _allowJoinQueue = value;
                NotifyPropertyChanged();
            }   
        }

        public bool AllowLeaveQueue
        {
            get => _allowLeaveQueue;
            set
            {
                _allowLeaveQueue = value;
                NotifyPropertyChanged();
            }
        }

        public string GameStatus
        {
            get => $"Game Status: {_gameStatus}";
            set
            {
                _gameStatus = value;
                NotifyPropertyChanged();
            }
        }

        public string PlayerName
        {
            get => $"Your player name: {_playerName}";
            set
            {
                _playerName = value;
                NotifyPropertyChanged();
            }
        }

        public string RowCardSelected
        {
            get => _cardRowSelected;
            set => _cardRowSelected = value;
        }

        public string PlayerNameSelected
        {
            get => _playerNameSelected;
            set => _playerNameSelected = value;
        }

        public BindingList<string> PlayersInLobby
        {
            get => _inGame ? FormatPlayersInLobbyWithCardTotal() : _playersInLobby;
            set
            {
                _playersInLobby = value;
                NotifyPropertyChanged();
            }
        }

        public BindingList<string> PlayerCards
        {
            get => _playerCards;
            set
            {
                _playerCards = value;
                NotifyPropertyChanged();
            }
        }

        public BindingList<string> CardRows
        {
            get => _cardRows;
            set
            {
                _cardRows = value;
                NotifyPropertyChanged();
            }
        }

        #endregion

        public MainViewModel()
        {
            _clientModel = new ClientModel();
            _gameStatus = "";
            _allowLeaveQueue = false;
            _allowJoinQueue = true;
            _playerName = "##Undefined##";
            _playersInLobby = new BindingList<string>();
            _playerCards = new BindingList<string>();
            _cardRows = new BindingList<string>();
            _allowStartGame = false;
            _serverListenerTokenSrc = new CancellationTokenSource();
            _numOfCardsPerPlayer = new Dictionary<string, int>();
            _inGame = false;
            _selectedUserCards = new List<DosCard>();
            _cardRowSelected = "";
            _playerNameSelected = "";

            JoinQueue = new DelegateCommand(RequestToJoinQueue, (object o) => AllowJoinQueue);
            LeaveQueue = new DelegateCommand(RequestToLeaveQueue, (object o) => AllowLeaveQueue);
            StartGame = new DelegateCommand(RequestToStartGame, (object o) => AllowStartGame);
            ChangeUserCardSelectionCommand = new DelegateCommand(ChangeUserCardSelection, (object o) => InGame);
            SendCards = new DelegateCommand(RequestCardMatch, (object o) => InGame);
            EndTurn = new DelegateCommand(RequestEndTurn, (object o) => InGame);
            AddRowCard = new DelegateCommand(RequestCardRowAddition, (object o) => InGame);
            WithdrawCard = new DelegateCommand(RequestCardDraw, (object o) => InGame);
            CallDos = new DelegateCommand(RequestDosCall, (object o) => InGame);
            GameStatus = "Offline";
        }

        #region Requests

        /// <summary>
        /// Gets called whenever the
        /// Join queue button is clicked.
        /// Requests a join queue command type
        /// to the server in order to join the lobby.
        /// If the server does not return any errors,
        /// it fetches the data returned by the server
        /// and starts listening in the background for
        /// more updates received be the server.
        /// </summary>
        /// <param name="o"></param>
        private void RequestToJoinQueue(object? o)
        {
            _clientModel = new ClientModel();
            _serverListenerTokenSrc = new CancellationTokenSource();
            GameStatus = "Connecting to lobby...";

            Dictionary<string, string>? data = _clientModel.JoinQueue();
            if (data != null && !data.ContainsKey("ErrorMessage"))
            {
                GameStatus = "Connected, waiting for players...";
                PlayerName = data["AssignedPlayerName"];

                BindingList<string>? playersInLobby = JsonSerializer.Deserialize<BindingList<string>>(data["CurrentPlayersInLobby"]);
                if (playersInLobby != null)
                {
                    PlayersInLobby = playersInLobby;
                    EnableStartGameBtnIfEnoughPlayers();
                }
                ToggleInQueue();
                ListenToServerResponses();
            }
            else if (data != null)
            {
                GameStatus = data["ErrorMessage"];
            }
            else
            {
                GameStatus = "Failed to connect";
            }
        }

        /// <summary>
        /// Notifies the server
        /// that the client is disconnecting from
        /// the lobby. Clears any data in the window.
        /// Triggered whenever the leave queue button
        /// is clicked.
        /// </summary>
        /// <param name="o"></param>
        private void RequestToLeaveQueue(object? o)
        {
            if (_clientModel.Disconnect())
            {
                ClearDataAfterDisconnect();
            }
            else
            {
                GameStatus = "Failed to disconnect from server";
            }
        }

        /// <summary>
        /// Sends a request of command type Start_Game
        /// to the server to start a game. Triggered
        /// whenever the start game button is clicked.
        /// </summary>
        /// <param name="o"></param>
        private void RequestToStartGame(object? o)
        {
            if (_clientModel.StartGame())
            {
                GameStatus = "Starting game...";
            }
            else
            {
                GameStatus = "An error has occured while trying to start game";
            }
        }

        /// <summary>
        /// Sends a request of type Card submission
        /// to the server to match cards selected.
        /// </summary>
        /// <param name="o"></param>
        private void RequestCardMatch(object? o)
        {
            if (_cardRowSelected != null)
            {
                if (_selectedUserCards.Count != 1 && _selectedUserCards.Count != 2)
                {
                    GameStatus = "Please select one or two of your cards to submit";
                }
                else if (_cardRowSelected == "")
                {
                    GameStatus = "Please select a card row to play on";
                }
                else
                {
                    if (_clientModel.SendCards(_selectedUserCards, (DosCard)_cardRowSelected))
                    {
                        GameStatus = "Cards submitted";
                    }
                }
            }
            else
            {
                GameStatus = "Please select one center row card to play";
            }
        }

        /// <summary>
        /// Sends a request of type row card
        /// addition to the server. Triggered
        /// whenever the add row card button
        /// is clicked.
        /// </summary>
        /// <param name="o"></param>
        private void RequestCardRowAddition(object? o)
        {
            if (_selectedUserCards.Count != 1)
            {
                GameStatus = "Can only submit one row card at a time.";
            }
            else
            {
                if (_clientModel.NotifyNewRowCardAddition(_selectedUserCards[0]))
                {
                    GameStatus = "Row card to add has been submitted";
                }
                else
                {
                    GameStatus = "There was an error while trying to submit a row card";
                }
            }
        }

        /// <summary>
        /// Sends a request of type
        /// draw card whenever the
        /// draw card button is clicked.
        /// </summary>
        /// <param name="o"></param>
        private void RequestCardDraw(object? o)
        {
            if (_clientModel.RequestCardWithdrawal())
            {
                GameStatus = "Requesting card withdrawal.";
            }
            else
            {
                GameStatus = "There was an error while trying to request server for card withdrawal";
            }
        }

        /// <summary>
        /// Sends a request of type
        /// end turn whenever the
        /// end turn button is clicked.
        /// </summary>
        /// <param name="o"></param>
        private void RequestEndTurn(object? o)
        {
            if (_clientModel.NotifyEndTurn())
            {
                GameStatus = "Ending turn...";
            }
            else
            {
                GameStatus = "There was an error while trying to notify the server to end turn.";
            }
        }

        /// <summary>
        /// Sends a request of type
        /// dos call whenever the Call Dos
        /// button is clicked.
        /// </summary>
        /// <param name="o"></param>
        private void RequestDosCall(object? o)
        {
            string playerName = "";
            if (PlayerNameSelected != null)
            {
                playerName = PlayerNameSelected.Split("|")[0].Trim();
            }
            else
            {
                playerName = _playerName;
            }
            if (_clientModel.RequestDosCall(playerName))
            {
                GameStatus = $"Calling Dos on {playerName}";
            }
            else
            {
                GameStatus = $"There was an error while trying to call Dos on {playerName}";
            }
        }

        /// <summary>
        /// Triggered whenever the selection
        /// of user cards changes.
        /// </summary>
        /// <param name="o"></param>
        private void ChangeUserCardSelection(object? o)
        {
            if (o != null)
            {
                System.Collections.IList newCardsSelected = (System.Collections.IList)o;
                List<DosCard> convertedList = new List<DosCard>();
                foreach (string card in newCardsSelected)
                {
                    convertedList.Add((DosCard)card);
                }
                _selectedUserCards = convertedList;
            }
        }

        #endregion

        #region Responses

        /// <summary>
        /// Listens to any updates from
        /// the server and calls specific
        /// functions accordingly.
        /// </summary>
        private async void ListenToServerResponses()
        {
            CancellationToken cancelToken = _serverListenerTokenSrc.Token;
            try
            {
                await Task.Run(() =>
                {
                    while (true)
                    {
                        cancelToken.ThrowIfCancellationRequested();
                        Protocol? serverResponse = _clientModel.ListenToServerResponse();

                        if (serverResponse != null)
                        {
                            switch (serverResponse.Command)
                            {
                                case Protocol.Commands.LOBBY_UPDATE:
                                    HandleLobbyUpdate(serverResponse);
                                    break;
                                case Protocol.Commands.GAME_UPDATE:
                                    HandleGameUpdateNotification(serverResponse);
                                    break;
                                case Protocol.Commands.INVALID_REQUEST:
                                    HandleErrorNotification(serverResponse);
                                    break;
                                case Protocol.Commands.GAME_FINISHED:
                                    HandleGameFinished(serverResponse);
                                    break;
                            }
                        }
                    }
                }, cancelToken);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Gets called whenever the server
        /// responds with a Game Finished command
        /// type. Clears any data in the screen
        /// and displays in a message box who the
        /// winner was.
        /// </summary>
        /// <param name="protocol"></param>
        private void HandleGameFinished(Protocol protocol)
        {
            if (protocol.Data != null)
            {
                string winnerName = protocol.Data["PlayerWinner"];
                string message = $"{winnerName} has won the game!!!";
                string caption = "End of game";

                ClearDataAfterDisconnect();

                MessageBox.Show(message, caption);
            }
        }

        /// <summary>
        /// Gets called whenever the server responds with a
        /// Lobby Update command type. Updates the list
        /// of players in the lobby with the data received
        /// from the server.
        /// </summary>
        /// <param name="protocol"></param>
        private void HandleLobbyUpdate(Protocol protocol)
        {
            if (protocol.Data != null)
            {
                BindingList<string>? newPlayersInLobby = JsonSerializer.Deserialize<BindingList<string>>(protocol.Data["CurrentPlayersInLobby"]);
                if (newPlayersInLobby != null)
                {
                    PlayersInLobby = newPlayersInLobby;
                    EnableStartGameBtnIfEnoughPlayers();
                }
            }
        }

        /// <summary>
        /// Gets called whenever the server responds with a
        /// Game Update command type. Fetches all the data
        /// inside the protocol, including the number of cards
        /// that each player has, the center row cards, the new
        /// user cards, any color bonuses, etc. Displays a message
        /// box if a player won the round.
        /// </summary>
        /// <param name="protocol"></param>
        private void HandleGameUpdateNotification(Protocol protocol)
        {
            InGame = true;
            if (AllowStartGame)
            {
                AllowStartGame = false;
            }
            if (protocol.Data != null)
            {
                List<DosCard>? playerCards = JsonSerializer.Deserialize<List<DosCard>>(protocol.Data["CurrCards"]);
                List<DosCard>? rowCards = JsonSerializer.Deserialize<List<DosCard>>(protocol.Data["RowCards"]);
                Dictionary<string, int>? numCardsPerPlayer = JsonSerializer.Deserialize<Dictionary<string, int>>(protocol.Data["NumCardsPerPlayer"]);
                int currBonus = JsonSerializer.Deserialize<int>(protocol.Data["CurrPlayerBonus"]);
                string currPlayerTurn = protocol.Data["CurrPlayerTurnName"];

                if (playerCards != null)
                {
                    PlayerCards = ConvertDosCardsToStringList(playerCards);
                }
                if (rowCards != null)
                {
                    CardRows = ConvertDosCardsToStringList(rowCards);
                }
                if (numCardsPerPlayer != null)
                {
                    _numOfCardsPerPlayer = numCardsPerPlayer;
                    NotifyPropertyChanged(nameof(PlayersInLobby));
                }
                if (currPlayerTurn == _playerName)
                {
                    GameStatus = $"It's your turn to play -- Current Player Bonus: {currBonus}\n";
                }
                else
                {
                    GameStatus = $"It's {currPlayerTurn} turn to play";
                }
                if (protocol.Data.ContainsKey("PlayerPenalizedByDosCall") && protocol.Data["PlayerPenalizedByDosCall"] == _playerName)
                {
                    GameStatus = $"{GameStatus} -- A player already called Dos on you. Received two cards as penalty.";
                }
                if (protocol.Data.ContainsKey("RoundWinner") && protocol.Data.ContainsKey("PlayerScores"))
                {
                    string message = $"Player {protocol.Data["RoundWinner"]} has won this round.\n";
                    string caption = "Winner!!!";

                    Dictionary<string, int>? playerScores = JsonSerializer.Deserialize<Dictionary<string, int>>(protocol.Data["PlayerScores"]);
                    if (playerScores != null)
                    {
                        message += "Player Scores:\n";
                        foreach (var playerScore in playerScores)
                        {
                            message += $"{playerScore.Key}: {playerScore.Value}\n";
                        }
                    }
                    MessageBox.Show(message, caption);
                }
            }
            
        }

        /// <summary>
        /// Gets called whenever the server responds with an Invalid
        /// Request command type. Displays erorr message in the
        /// game status.
        /// </summary>
        /// <param name="protocol"></param>
        private void HandleErrorNotification(Protocol protocol)
        {
            if (protocol.Data != null)
            {
                GameStatus = protocol.Data["ErrorMessage"];
            }
        }

        #endregion

        #region HelperFunc

        /// <summary>
        /// Clears all data in the window
        /// after user disconnects from
        /// ther server.
        /// </summary>
        private void ClearDataAfterDisconnect()
        {
            GameStatus = "Offline";
            PlayerName = "##Undefined##";
            PlayersInLobby = new BindingList<string>();
            PlayerCards = new BindingList<string>();
            CardRows = new BindingList<string>();
            AllowStartGame = false;
            ToggleInQueue();
            InGame = false;
            _serverListenerTokenSrc.Cancel();
        }

        /// <summary>
        /// Adds a card count to each
        /// player name when the user
        /// is playing.
        /// </summary>
        /// <returns></returns>
        private BindingList<string> FormatPlayersInLobbyWithCardTotal()
        {
            BindingList<string> formatedPlayerList = new BindingList<string>();
            foreach (string playerName in _playersInLobby)
            {
                formatedPlayerList.Add($"{playerName} | Total Cards: {_numOfCardsPerPlayer[playerName]}");
            }
            return formatedPlayerList;
        }

        /// <summary>
        /// Converts a list of 
        /// DosCard objects to
        /// a binding list of strings
        /// in order to display
        /// them in the window.
        /// </summary>
        /// <param name="cards"></param>
        /// <returns></returns>
        private BindingList<string> ConvertDosCardsToStringList(List<DosCard> cards)
        {
            BindingList<string> cardsAsStrList = new BindingList<string>();
            foreach (DosCard card in cards)
            {
                cardsAsStrList.Add(card.ToString());
            }
            return cardsAsStrList;
        }

        /// <summary>
        /// Toggles properties
        /// AllowJoinQueue and AllowLeaveQueue.
        /// Generally called whenever a player
        /// connects or disconnects from the lobby.
        /// </summary>
        private void ToggleInQueue()
        {
            AllowJoinQueue = !AllowJoinQueue;
            AllowLeaveQueue = !AllowLeaveQueue;
        }
        
        /// <summary>
        /// Enables the Start Game button
        /// if the number of players in the
        /// lobby is >= 2.
        /// </summary>
        private void EnableStartGameBtnIfEnoughPlayers()
        {
            if (PlayersInLobby.Count >= 2)
            {
                AllowStartGame = true;
            } 
            else
            {
                AllowStartGame = false;
            }
        }

        /// <summary>
        /// This method is in charge of notifying the view 
        /// that a given property has changed.
        /// </summary>
        /// <param name="propertyName"></param>
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
