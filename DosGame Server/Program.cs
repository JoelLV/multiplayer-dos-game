using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DosGame_Server
{
    public class Program
    {
        public static void Main()
        {
            WebServer server = new WebServer();

            server.Start();
        }
    }
}
