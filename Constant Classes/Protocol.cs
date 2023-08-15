using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class Protocol
{
    public enum Commands
    {
        JOIN_QUEUE = 0,
        DISCONNECT = 1,
        INVALID_REQUEST = 2,
        Ok = 3,
        START_GAME = 4,
        CARD_SUBMISSION = 5,
        LOBBY_UPDATE = 6,
        GAME_UPDATE = 7,
        END_TURN = 8,
        ROW_CARD_ADDITION = 9,
        DRAW_CARD = 10,
        CALL_DOS = 11,
        GAME_FINISHED = 12,
    }
    public Commands Command { get; set; }
    public Dictionary<string, string>? Data { get; set; }
}
