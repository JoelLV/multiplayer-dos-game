using Constant_Classes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DosGame_Server
{
    internal class GameEngine
    {
        private Queue<DosCard> _deck;
        private Dictionary<int, List<DosCard>> _cardsPerPlayer;
        private Dictionary<int, int> _playerScores;
        private Dictionary<int, bool> _dosCallProtectionPerPlayer;
        private Queue<int> _playerTurns;
        private List<DosCard> _rowCards;
        private List<int> _playerIds;
        private int _playerBonuses;
        private bool _canDraw;
        private bool _canMatch;
        private bool _cardDrawnDiscarded;
        private bool _addDosCallPenaltyAtEndOfTurn;
        private int _playerIdPenalizedByDosCall;
        private int _playerIdWinner;
        private bool _gameFinished;

        #region ReadonlyProperties

        public int PlayerIdPenalizedByDosCall
        {
            get => _playerIdPenalizedByDosCall;
        }

        public bool GameFinished
        {
            get => _gameFinished;
        }

        public int CurrPlayerId
        {
            get => _playerTurns.Peek();
        }

        public Dictionary<int, List<DosCard>> CardsPerPlayer
        {
            get => _cardsPerPlayer;
        }

        public List<DosCard> RowCards
        {
            get => _rowCards;
        }

        public Dictionary<int, int> PlayerScores
        {
            get => _playerScores;
        }

        public int CurrPlayerBonus
        {
            get => _playerBonuses;
        }

        public int PlayerIdWinner
        {
            get => _playerIdWinner;
        }

        #endregion

        public GameEngine(List<int> playerIds)
        {
            _playerIds = playerIds;
            _playerScores = InitializePlayerScores(playerIds);
            _gameFinished = false;

            _deck = new Queue<DosCard>();
            _cardsPerPlayer = new Dictionary<int, List<DosCard>>();
            _dosCallProtectionPerPlayer = new Dictionary<int, bool>();
            _playerTurns = new Queue<int>();
            _rowCards = new List<DosCard>();

            RestartRound();
        }

        #region GameActions

        /// <summary>
        /// Uses official Dos rules to make
        /// a match. Returns empty string
        /// if no error was found. Returns
        /// error message if an error is
        /// found.
        /// </summary>
        /// <param name="userCards"></param>
        /// <param name="userId"></param>
        /// <returns>Error message if error detected</returns>
        public string PlayCards(List<DosCard> userCards, DosCard rowCard, int userId)
        {
            if (userId != _playerTurns.Peek())
            {
                return "Cannot play. It is not your turn yet.";
            }
            else if (userCards.Count != 1 && userCards.Count != 2)
            {
                return "Invalid number of cards to play.";
            }
            else if (!CardInDeck(rowCard, _rowCards))
            {
                return "Invalid row card given.";
            }
            else if (!_canMatch)
            {
                return "Cannot match a card after adding a new card to the center row";
            }
            else if (_gameFinished)
            {
                return "Game has already finished";
            }
            else
            {
                foreach (DosCard card in userCards)
                {
                    if (!CardInDeck(card, CardsPerPlayer[userId]))
                    {
                        return "User does not have given card.";
                    }
                }
                try
                {
                    AttemptToPlayCard(userCards, rowCard, userId);
                    _canDraw = false;
                    _cardDrawnDiscarded = true;
                    CheckIfWon(userId);
                    return "";
                }
                catch (ArgumentException ex)
                {
                    // An error was found when applying the game rules to this move.
                    return ex.Message;
                }
                
            }
        }

        /// <summary>
        /// Creates a dictionary of player names
        /// as keys and card count as values
        /// using private field _cardsPerPlayer and
        /// private field _playerNames from WebServer
        /// class.
        /// </summary>
        /// <param name="playerNames"></param>
        /// <returns></returns>
        public Dictionary<string, int> GetNumCardsPerPlayer(Dictionary<int, string> playerNames)
        {
            Dictionary<string, int> numCardsPerPlayer = new Dictionary<string, int>();
            foreach (var cards in _cardsPerPlayer)
            {
                numCardsPerPlayer.Add(playerNames[cards.Key], cards.Value.Count);
            }
            return numCardsPerPlayer;
        }

        /// <summary>
        /// Moves current player's
        /// turn back to the back
        /// of the player turn
        /// queue. If no error is
        /// found, an empty string is
        /// returned, otherwise, the
        /// error message is returned.
        /// If the number of row cards is
        /// less than two, it will be filled
        /// up to two.
        /// </summary>
        /// <returns></returns>
        public string EndPlayerTurn(int playerId)
        {
            if (_playerBonuses > 0)
            {
                return "You must use all of your card bonuses before ending your turn.";
            }
            else if (playerId != _playerTurns.Peek())
            {
                return "It is not your turn yet.";
            }
            else if (_canDraw)
            {
                return "You must draw a card if you were not able to match any cards.";
            }
            else if (!_cardDrawnDiscarded)
            {
                return "You must put a card in the center row if you drew a card from the deck.";
            }
            else if (_gameFinished)
            {
                return "Game has already finished";
            }
            else
            {
                if (_addDosCallPenaltyAtEndOfTurn)
                {
                    _cardsPerPlayer[playerId].Add(_deck.Dequeue());
                    _cardsPerPlayer[playerId].Add(_deck.Dequeue());
                }
                _playerTurns.Enqueue(_playerTurns.Dequeue());
                while (_rowCards.Count < 2)
                {
                    _rowCards.Add(_deck.Dequeue());
                }
                RestartTurn();

                return "";
            }
        }

        /// <summary>
        /// Adds a given card
        /// to the Center row according
        /// to the rules of Dos. If
        /// an error is found, a string
        /// with the message of the error
        /// is returned, otherwise the
        /// method returns an empty string.
        /// </summary>
        /// <param name="newRowCard"></param>
        /// <returns></returns>
        public string AddCardToRow(DosCard newRowCard, int playerId)
        {
            if (playerId != _playerTurns.Peek())
            {
                return "Cannot submit a card to the center row because it is not your turn yet.";
            }
            else if (!CardInDeck(newRowCard, _cardsPerPlayer[playerId]))
            {
                return "User does not have given card.";
            }
            else if (_gameFinished)
            {
                return "Game has already finished";
            }
            else
            {
                if (_playerBonuses > 0)
                {
                    --_playerBonuses;
                    _canMatch = false;
                    TransferUserCardToCenterRow(_cardsPerPlayer[playerId], newRowCard);
                    UpdateDosCallProtection(playerId);
                    CheckIfWon(playerId);
                    return "";
                }
                else if (!_cardDrawnDiscarded)
                {
                    _canMatch = false;
                    _cardDrawnDiscarded = true;
                    TransferUserCardToCenterRow(_cardsPerPlayer[playerId], newRowCard);
                    UpdateDosCallProtection(playerId);
                    return "";
                }
                else
                {
                    return "You must have color bonuses or must have drawn a card in order to add a card to the center row.";
                }
            }
        }

        /// <summary>
        /// Gets a card
        /// from the deck
        /// and gives it to the
        /// player's hand according
        /// to the given player id.
        /// Returns empty string if no
        /// errors are found. Otherwise,
        /// the message of the error is
        /// returned.
        /// </summary>
        /// <param name="playerId"></param>
        /// <returns></returns>
        public string DrawCard(int playerId)
        {
            if (playerId != _playerTurns.Peek())
            {
                return "It is not your turn yet.";
            }
            else if (!_canDraw)
            {
                return "You cannot draw a card if you already matched a center row card or you already drew from the deck.";
            }
            else if (_gameFinished)
            {
                return "Game has already finished";
            }
            else
            {
                _canDraw = false;
                _cardDrawnDiscarded = false;
                _cardsPerPlayer[playerId].Add(_deck.Dequeue());
                UpdateDosCallProtection(playerId);
                return "";
            }
        }

        /// <summary>
        /// If only one argument is given,
        /// a flag is set in order for the
        /// given player to not suffer from
        /// Dos call penalty. If a second
        /// argument is given, we will check
        /// whether the second player id does
        /// not have its flag set, if it is not
        /// set, we will apply the Dos call penalty
        /// immediately or eventually, depending on
        /// who's turn it is. An empty string is returned
        /// if no error was found, otherwise the error message
        /// is returned.
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="playerIdToPunish"></param>
        /// <returns></returns>
        public string CallDos(int playerId, int playerIdToPunish = -1)
        {
            if (!_gameFinished)
            {
                if (playerIdToPunish == -1 || playerId == playerIdToPunish)
                {
                    _dosCallProtectionPerPlayer[playerId] = true;
                    return "";
                }
                else
                {
                    if (!_dosCallProtectionPerPlayer[playerIdToPunish])
                    {
                        if (_playerTurns.Peek() == playerIdToPunish)
                        {
                            _addDosCallPenaltyAtEndOfTurn = true;
                            _dosCallProtectionPerPlayer[playerIdToPunish] = true;
                        }
                        else
                        {
                            _cardsPerPlayer[playerIdToPunish].Add(_deck.Dequeue());
                            _cardsPerPlayer[playerIdToPunish].Add(_deck.Dequeue());
                            _dosCallProtectionPerPlayer[playerIdToPunish] = true;
                        }
                        _playerIdPenalizedByDosCall = playerIdToPunish;
                        return "";
                    }
                    else
                    {
                        return "Cannot call Dos on selected player.";
                    }
                }
            }
            else
            {
                return "Game has already finished";
            }
        }

        /// <summary>
        /// Creates new deck
        /// with new hands according
        /// to the saved player ids.
        /// Restarts all flags.
        /// </summary>
        public void RestartRound()
        {
            _deck = CreateFullDeck();
            _cardsPerPlayer = DistributeCards(_playerIds);
            _playerTurns = ShufflePlayerTurns(_playerIds);
            _dosCallProtectionPerPlayer = InitializeDosCallProtection(_playerIds);
            _rowCards = new List<DosCard>() { _deck.Dequeue(), _deck.Dequeue() };
            _playerIdWinner = -1;

            RestartTurn();
        }

        /// <summary>
        /// Removes cards of given player and
        /// anything related to the given player.
        /// </summary>
        /// <param name="playerId"></param>
        public void RemovePlayer(int playerId)
        {
            List<DosCard> cards = _cardsPerPlayer[playerId];
            foreach (DosCard card in cards)
            {
                _deck.Enqueue(card);
            }

            _cardsPerPlayer.Remove(playerId);
            _playerScores.Remove(playerId);
            _dosCallProtectionPerPlayer.Remove(playerId);

            if (_playerTurns.Peek() == playerId)
            {
                while (_rowCards.Count < 2)
                {
                    _rowCards.Add(_deck.Dequeue());
                }
                RestartTurn();
            }

            Queue<int> newPlayerTurns = new Queue<int>();
            foreach (int id in _playerTurns)
            {
                if (id != playerId)
                {
                    newPlayerTurns.Enqueue(id);
                }
            }
            _playerTurns = newPlayerTurns;

            for (int i = 0; i < _playerIds.Count; i++)
            {
                if (_playerIds[i] == playerId)
                {
                    _playerIds.RemoveAt(i);
                    break;
                }
            }

            if (_playerIds.Count < 2)
            {
                _gameFinished = true;
                _playerIdWinner = _playerIds[0];
            }
        }

        #endregion

        #region HelperFunctions

        /// <summary>
        /// Sets the winner
        /// player id if
        /// the given player id
        /// has 0 cards left and
        /// there was no Dos call
        /// penalty. Updates
        /// scoreboard.
        /// </summary>
        /// <param name="playerId"></param>
        private void CheckIfWon(int playerId)
        {
            if (_cardsPerPlayer[playerId].Count == 0 && !_addDosCallPenaltyAtEndOfTurn)
            {
                _playerIdWinner = playerId;
                int totalPoints = _playerScores[playerId];
                foreach (var player in _cardsPerPlayer)
                {
                    if (player.Key != playerId)
                    {
                        List<DosCard> cards = player.Value;
                        foreach (DosCard card in cards)
                        {
                            if (card.Color == DosCard.CardColor.Wild)
                            {
                                totalPoints += 20;
                            }
                            else if (card.Number == "#")
                            {
                                totalPoints += 40;
                            }
                            else
                            {
                                totalPoints += int.Parse(card.Number);
                            }
                        }
                    }
                }
                _playerScores[playerId] = totalPoints;
                if (_playerScores[playerId] >= 200)
                {
                    _gameFinished = true;
                }
            }
        }

        /// <summary>
        /// Restarts game flags
        /// for next turn.
        /// </summary>
        private void RestartTurn()
        {
            _playerBonuses = 0;
            _canDraw = true;
            _cardDrawnDiscarded = true;
            _canMatch = true;
            _addDosCallPenaltyAtEndOfTurn = false;
            _playerIdPenalizedByDosCall = -1;
        }

        /// <summary>
        /// Updates protection flag
        /// for given player id
        /// if the number of cards of
        /// given player equals
        /// to two.
        /// </summary>
        /// <param name="playerId"></param>
        private void UpdateDosCallProtection(int playerId)
        {
            if (_cardsPerPlayer[playerId].Count == 2)
            {
                _dosCallProtectionPerPlayer[playerId] = false;
            }
        }

        /// <summary>
        /// Searches for given user hand
        /// the given card to remove. When
        /// found, the card will be removed
        /// from the user list and will be
        /// placed in the center row.
        /// </summary>
        /// <param name="userHand"></param>
        /// <param name="cardToRemove"></param>
        private void TransferUserCardToCenterRow(List<DosCard> userHand, DosCard cardToRemove)
        {
            for (int i = 0; i < userHand.Count; i++)
            {
                DosCard card = userHand[i];
                if (card.Color == cardToRemove.Color && card.Number == cardToRemove.Number)
                {
                    userHand.RemoveAt(i);
                    _rowCards.Add(cardToRemove);
                    break;
                }
            }
        }

        /// <summary>
        /// Creates a map with given player 
        /// ids that store whether a certain
        /// player is susceptable to Dos
        /// call penalty.
        /// </summary>
        /// <param name="playerIds"></param>
        /// <returns></returns>
        private Dictionary<int, bool> InitializeDosCallProtection(List<int> playerIds)
        {
            Dictionary<int, bool> dosCallProtectionPerPlayer = new Dictionary<int, bool>();
            foreach (int playerId in playerIds)
            {
                dosCallProtectionPerPlayer.Add(playerId, true);
            }

            return dosCallProtectionPerPlayer;
        }

        /// <summary>
        /// Returns a Dictionary with
        /// keys being the ids of given
        /// players and their values as 0.
        /// </summary>
        /// <param name="playerIds"></param>
        /// <returns></returns>
        private Dictionary<int, int> InitializePlayerScores(List<int> playerIds)
        {
            Dictionary<int, int> playerScores = new Dictionary<int, int>();
            foreach (int id in playerIds)
            {
                playerScores.Add(id, 0);
            }

            return playerScores;
        }

        /// <summary>
        /// Creates a shuffled Queue of Dos cards according
        /// to the rules of Dos.
        /// </summary>
        /// <returns>A shuffled Queue representing a shuffled Dos deck</returns>
        private Queue<DosCard> CreateFullDeck()
        {
            List<DosCard> deck = new List<DosCard>();
            DosCard.CardColor[] cardColors = { DosCard.CardColor.Red, DosCard.CardColor.Blue, DosCard.CardColor.Green, DosCard.CardColor.Yellow };
            string[] cardNums3 = { "1", "3", "4", "5" };
            string[] cardNums2 = { "6", "7", "8", "9", "10", "#" };

            foreach (DosCard.CardColor color in cardColors)
            {
                foreach (string num in cardNums3)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        deck.Add(new DosCard(color, num));
                    }
                }
                foreach (string num in cardNums2)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        deck.Add(new DosCard(color, num));
                    }
                }
            }
            for (int i = 0; i < 12; i++)
            {
                deck.Add(new DosCard(DosCard.CardColor.Wild, "2"));
            }
            deck = ShuffleDeck(deck);

            return new Queue<DosCard>(deck);
            
        }

        /// <summary>
        /// Creates a new list representing
        /// a shuffled version of the given
        /// deck of DosCards.
        /// </summary>
        /// <param name="deck"></param>
        /// <returns>Shuffled list</returns>
        private List<DosCard> ShuffleDeck(List<DosCard> deck)
        {
            Random rand = new Random();
            return deck.OrderBy(_ => rand.Next()).ToList();
        }

        /// <summary>
        /// Shuffles the number of players
        /// in the lobby and puts elements
        /// in the list to a queue.
        /// </summary>
        /// <param name="players"></param>
        /// <returns></returns>
        private Queue<int> ShufflePlayerTurns(List<int> players)
        {
            Random rand = new Random();
            return new Queue<int>(players.OrderBy(_ => rand.Next()).ToList());
        }

        /// <summary>
        /// Distributes the shuffled cards
        /// inside the deck to each player.
        /// </summary>
        /// <param name="playerIds">List of player ids passed by the web server</param>
        /// <returns>A list of lists representing the cards that each player has.</returns>
        private Dictionary<int, List<DosCard>> DistributeCards(List<int> playerIds)
        {
            Dictionary<int, List<DosCard>> cardsPerPlayer = new Dictionary<int, List<DosCard>>();
            for (int i = 0; i < playerIds.Count; i++)
            {
                List<DosCard> cardsForPlayer = new List<DosCard>();
                for (int ii = 0; ii < 7; ii++)
                {
                    cardsForPlayer.Add(_deck.Dequeue());
                }
                cardsPerPlayer.Add(playerIds[i], cardsForPlayer);
            }

            return cardsPerPlayer;
        }

        /// <summary>
        /// Checks if given card is inside
        /// of given deck.
        /// </summary>
        /// <param name="cardToCheck"></param>
        /// <param name="deck"></param>
        /// <returns></returns>
        private bool CardInDeck(DosCard cardToCheck, List<DosCard> deck)
        {
            foreach (DosCard card in deck)
            {
                if ((cardToCheck.Number == card.Number) && (cardToCheck.Color == card.Color))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Attempts to match a single
        /// card with the given row card.
        /// Throws an ArgumentException
        /// if cards do not match.
        /// If a color match bonus is found,
        /// _playerBonuses is incremented.
        /// </summary>
        /// <param name="userCards"></param>
        /// <param name="rowCard"></param>
        /// <exception cref="ArgumentException"></exception>
        private void AttemptSingleMatch(DosCard userCards, DosCard rowCard)
        {
            if (userCards.Number != "#" && rowCard.Number != "#")
            {
                if (userCards.Number != rowCard.Number)
                {
                    throw new ArgumentException("Card given does not match selected row card.");
                }
            }
            if (userCards.Color != DosCard.CardColor.Wild && rowCard.Color != DosCard.CardColor.Wild)
            {
                if (userCards.Color == rowCard.Color)
                {
                    ++_playerBonuses;
                }
            }
            else
            {
                ++_playerBonuses;
            }
        }

        /// <summary>
        /// Attempts to match two
        /// cards with a given row card.
        /// Throws an ArgumentException error
        /// if an error is found. Applies
        /// a Double Match Bonus if
        /// scenario applies.
        /// </summary>
        /// <param name="userCards"></param>
        /// <param name="rowCard"></param>
        /// <exception cref="ArgumentException"></exception>
        private void AttemptDoubleMatch(List<DosCard> userCards, DosCard rowCard)
        {
            if (userCards[0].Number == "10" || userCards[1].Number == "10")
            {
                throw new ArgumentException("Cannot play a double match when a card is a 10.");
            }
            else if (rowCard.Number != "#")
            {
                if (userCards[0].Number != "#" && userCards[1].Number != "#")
                {
                    if (int.Parse(userCards[0].Number) + int.Parse(userCards[1].Number) != int.Parse(rowCard.Number))
                    {
                        throw new ArgumentException("Cards do not add up to the number in the row card.");
                    }
                }
                else if ((userCards[0].Number != "#" && int.Parse(userCards[0].Number) >= int.Parse(rowCard.Number))
                    || (userCards[1].Number != "#" && int.Parse(userCards[1].Number) >= int.Parse(rowCard.Number)))
                {
                    throw new ArgumentException("Cannot match these two cards because one of them is already greater than or equal to the row card number.");
                }
            }
            else
            {
                // Number of row card is #.
                if (userCards[0].Number != "#" && userCards[1].Number != "#")
                {
                    if (int.Parse(userCards[0].Number) + int.Parse(userCards[1].Number) > 10)
                    {
                        throw new ArgumentException("The sum of given two cards is greater than the maximum number allowed.");
                    }
                }
            }

            if (rowCard.Color == DosCard.CardColor.Wild)
            {
                if (userCards[0].Color == userCards[1].Color
                    || userCards[0].Color == DosCard.CardColor.Wild
                    || userCards[1].Color == DosCard.CardColor.Wild)
                {
                    ApplyDoubleMatchPenalty();
                    ++_playerBonuses;
                }
            }
            else
            {
                if (userCards[0].Color == rowCard.Color && userCards[1].Color == rowCard.Color)
                {
                    ApplyDoubleMatchPenalty();
                    ++_playerBonuses;
                }
                else if (userCards[0].Color == DosCard.CardColor.Wild && userCards[1].Color == rowCard.Color
                    || userCards[1].Color == DosCard.CardColor.Wild && userCards[0].Color == rowCard.Color)
                {
                    ApplyDoubleMatchPenalty();
                    ++_playerBonuses;
                }
                else if (userCards[0].Color == DosCard.CardColor.Wild && userCards[1].Color == DosCard.CardColor.Wild)
                {
                    ApplyDoubleMatchPenalty();
                    ++_playerBonuses;
                }
            }
        }

        /// <summary>
        /// Gives all players except the
        /// current player a Double Match
        /// penaly, which is drawing one
        /// card from the deck.
        /// </summary>
        private void ApplyDoubleMatchPenalty()
        {
            foreach (var player in _cardsPerPlayer)
            {
                if (player.Key != _playerTurns.Peek() && _deck.Count > 0)
                {
                    _cardsPerPlayer[player.Key].Add(_deck.Dequeue());
                    UpdateDosCallProtection(player.Key);
                }
            }
        }

        /// <summary>
        /// Removes cards specified in cardsToRemove parameter
        /// from the given collection of DosCard. The items
        /// removed will be returned to the deck.
        /// </summary>
        /// <param name="deckToRemoveFrom"></param>
        /// <param name="cardsToRemove"></param>
        private void ReturnCardsToDeck(List<DosCard> deckToRemoveFrom, List<DosCard> cardsToRemove)
        {
            for (int i = 0; i < cardsToRemove.Count; i++)
            {
                for (int ii = 0; ii < deckToRemoveFrom.Count; ii++)
                {
                    DosCard cardToRemove = cardsToRemove[i];
                    DosCard cardInDeck = deckToRemoveFrom[ii];
                    if (cardInDeck.Color == cardToRemove.Color && cardInDeck.Number == cardToRemove.Number)
                    {
                        deckToRemoveFrom.RemoveAt(ii);
                        _deck.Enqueue(cardInDeck);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to play given card/s.
        /// If the move violates a Dos rule,
        /// an exception is thrown. If
        /// the move is valid, the given
        /// row card will be discarded from
        /// the _rowCards list, and the given
        /// userCards will be discarded
        /// according to the given user id.
        /// All discarded cards will be
        /// put back to the deck.
        /// </summary>
        /// <param name="userCards"></param>
        /// <param name="rowCard"></param>
        /// <param name="userId"></param>
        /// <exception cref="ArgumentException"></exception>
        private void AttemptToPlayCard(List<DosCard> userCards, DosCard rowCard, int userId)
        {
            if (userCards.Count == 1)
            {
                AttemptSingleMatch(userCards[0], rowCard);
            }
            else
            {
                // If branch reached, userCards has a length of 2.
                AttemptDoubleMatch(userCards, rowCard);
            }
            ReturnCardsToDeck(_cardsPerPlayer[userId], userCards);
            ReturnCardsToDeck(_rowCards, new List<DosCard> { rowCard });
            UpdateDosCallProtection(userId);
        }

        #endregion
    }
}
