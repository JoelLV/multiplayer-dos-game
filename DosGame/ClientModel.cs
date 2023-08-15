using Constant_Classes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DosGame_UI
{
    internal class ClientModel
    {
        private TcpClient _clientSocket;

        public ClientModel()
        {
            _clientSocket = new TcpClient();
        }

        /// <summary>
        /// Attempts to connect
        /// to the server and join the lobby.
        /// Returns the data received from the server
        /// if the connection is successful.
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, string>? JoinQueue()
        {
            try
            {
                _clientSocket.Connect("127.0.0.1", 8888);
                NetworkStream stream = _clientSocket.GetStream();

                Protocol joinQueueProtocol = new Protocol
                {
                    Command = Protocol.Commands.JOIN_QUEUE
                };
                string message = JsonSerializer.Serialize(joinQueueProtocol);

                SendData(stream, message);

                Protocol? response = ReadData(stream);
                if (response != null && response.Data != null)
                {
                    return response.Data;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Closes client socket.
        /// </summary>
        /// <returns></returns>
        public bool Disconnect()
        {
            try
            {
                _clientSocket?.Close();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Sends a START GAME 
        /// protocol to the server
        /// to indicate that the
        /// client wants to start
        /// a game session.
        /// </summary>
        /// <returns></returns>
        public bool StartGame()
        {
            try
            {
                Protocol protocol = new Protocol
                {
                    Command = Protocol.Commands.START_GAME,
                };
                string message = JsonSerializer.Serialize(protocol);
                SendData(_clientSocket.GetStream(), message);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Sends a CARD_SUBMISSION
        /// protocol to the server
        /// to attempt a match of
        /// given user cards on
        /// a row card.
        /// </summary>
        /// <param name="userCards"></param>
        /// <param name="rowCard"></param>
        /// <returns></returns>
        public bool SendCards(List<DosCard> userCards, DosCard rowCard)
        {
            try
            {
                Protocol protocol = new Protocol
                {
                    Command = Protocol.Commands.CARD_SUBMISSION,
                    Data = new Dictionary<string, string>
                    {
                        { "CardsSubmitted", JsonSerializer.Serialize(userCards) },
                        { "RowCardToPlay", JsonSerializer.Serialize(rowCard) }
                    }
                };
                string message = JsonSerializer.Serialize(protocol);
                SendData(_clientSocket.GetStream(), message);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Returns any data that was
        /// received from the server.
        /// </summary>
        /// <returns></returns>
        public Protocol? ListenToServerResponse()
        {
            return ReadData(_clientSocket.GetStream());
        }

        /// <summary>
        /// Sends a END_TURN
        /// protocol that notifies
        /// the server to end the
        /// current player's turn.
        /// </summary>
        /// <returns></returns>
        public bool NotifyEndTurn()
        {
            try
            {
                Protocol protocol = new Protocol
                {
                    Command = Protocol.Commands.END_TURN
                };
                string message = JsonSerializer.Serialize(protocol);
                SendData(_clientSocket.GetStream(), message);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Sends a ROW_CARD_ADDITION
        /// protocol to the server
        /// in order to notify the
        /// server that the user
        /// wants to add given
        /// row card to the center row.
        /// </summary>
        /// <param name="rowCard"></param>
        /// <returns></returns>
        public bool NotifyNewRowCardAddition(DosCard rowCard)
        {
            try
            {
                Protocol protocol = new Protocol
                {
                    Command = Protocol.Commands.ROW_CARD_ADDITION,
                    Data = new Dictionary<string, string>()
                    {
                        { "RowCardToAdd",  JsonSerializer.Serialize(rowCard)}
                    }
                };
                string message = JsonSerializer.Serialize(protocol);
                SendData(_clientSocket.GetStream(), message);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Sends a DRAW_CARD protocol
        /// that notifies the server
        /// that the user wants to draw
        /// a card from the deck.
        /// </summary>
        /// <returns></returns>
        public bool RequestCardWithdrawal()
        {
            try
            {
                Protocol protocol = new Protocol()
                {
                    Command = Protocol.Commands.DRAW_CARD,
                };
                string message = JsonSerializer.Serialize(protocol);
                SendData(_clientSocket.GetStream(), message);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Sends a CALL_DOS protocol
        /// to the server in order to
        /// notify the server that the
        /// user called Dos. If the
        /// parameter is an empty string,
        /// the method does not send any
        /// data inside the protocol. If
        /// a name is given, then
        /// the name will be added as
        /// part of the data of the protocol.
        /// </summary>
        /// <param name="playerNameToCallOn"></param>
        /// <returns></returns>
        public bool RequestDosCall(string playerNameToCallOn)
        {
            try
            {
                Protocol protocol = new Protocol
                {
                    Command = Protocol.Commands.CALL_DOS
                };
                if (playerNameToCallOn != "")
                {
                    protocol.Data = new Dictionary<string, string>()
                    {
                        { "PlayerToPunish", playerNameToCallOn }
                    };
                }

                string message = JsonSerializer.Serialize(protocol);
                SendData(_clientSocket.GetStream(), message);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Sends given message using
        /// given stream.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="message"></param>
        private void SendData(NetworkStream stream, string message)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        /// <summary>
        /// Reads data from the server and
        /// returns a protocol object containing
        /// the data received from the server.
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        private Protocol? ReadData(NetworkStream stream)
        {
            byte[] bytes = new byte[16384];

            stream.Read(bytes, 0, bytes.Length);

            string data = Encoding.UTF8.GetString(bytes);
            data = data.Replace("\0", "");

            Protocol? jsonResponse = null;
            try
            {
                jsonResponse = JsonSerializer.Deserialize<Protocol>(data);
            }
            catch (JsonException)
            {
            }
            return jsonResponse;
        }
    }
}
