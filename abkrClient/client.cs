﻿using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using abkr.ClientLogger;

namespace abkr.Client
{
    class Client
    {
        private static Logger logger = new Logger("C:/Users/bfcsa/github-classroom/2023-AB2-projects/ab2-project-tbd/abkrClient/client_logger.txt");
        static async Task Main(string[] args)
        {
            // Connect to server
            TcpClient client = new TcpClient();
            await client.ConnectAsync(IPAddress.Parse("127.0.0.1"), 1234);

            logger.LogMessage("Connected to server. Enter SQL statements or type 'exit' to quit:");

            using NetworkStream stream = client.GetStream();
            using StreamReader reader = new StreamReader(stream, Encoding.ASCII);
            using StreamWriter writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

            while (true)
            {
                // Read SQL statement from the command line
                Console.Write("> ");
                string? sqlStatement = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(sqlStatement))
                {
                    continue;
                }

                if (sqlStatement.ToLower() == "exit")
                {
                    break;
                }

                // Send SQL statement to server
                await writer.WriteLineAsync(sqlStatement);

                // Receive response from server
                string? response = await reader.ReadLineAsync();
                logger.LogMessage($"{response}");
            }

            // Close connection
            client.Close();
            logger.LogMessage("Disconnected from server.");
        }
    }
}
