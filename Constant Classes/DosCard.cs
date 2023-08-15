using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Constant_Classes
{
    public class DosCard
    {
        private CardColor _color;
        private string _number;

        public enum CardColor
        {
            Blue = 0,
            Green = 1,
            Red = 2,
            Yellow = 3,
            Wild = 4,
        }

        public CardColor Color
        {
            get => _color;
        }

        public string Number
        {
            get => _number;
        }

        public DosCard(CardColor color, string number)
        {
            _color = color;
            _number = number;
        }

        public override string ToString()
        {
            string cardStr = "";
            switch(_color)
            {
                case CardColor.Blue:
                    cardStr = "Blue";
                    break;
                case CardColor.Green:
                    cardStr = "Green";
                    break;
                case CardColor.Red:
                    cardStr = "Red";
                    break;
                case CardColor.Yellow:
                    cardStr = "Yellow";
                    break;
                case CardColor.Wild:
                    cardStr = "Wild";
                    break;
            }
            return $"{cardStr} {_number}";
        }

        public static explicit operator DosCard(string dosCardAsStr)
        {
            string[] elems = dosCardAsStr.Split(' ');
            switch (elems[0])
            {
                case "Blue":
                    return new DosCard(CardColor.Blue, elems[1]);
                case "Green":
                    return new DosCard(CardColor.Green, elems[1]);
                case "Red":
                    return new DosCard(CardColor.Red, elems[1]);
                case "Yellow":
                    return new DosCard(CardColor.Yellow, elems[1]);
                case "Wild":
                    return new DosCard(CardColor.Wild, elems[1]);
                default:
                    throw new ArgumentException("Invalid string format for casting to a DosCard object");
            }
        }
    }
}
