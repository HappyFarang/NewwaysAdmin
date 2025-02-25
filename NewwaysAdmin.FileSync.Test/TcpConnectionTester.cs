using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NewwaysAdmin.FileSync.Test
{
    public class TcpTester
    {
        public static async Task RunServer(int port)
        {
            // Explicitly bind to all interfaces
            var listener = new TcpListener(IPAddress.Any, port);
            Console.WriteLine($"Starting server on port {port}...");

            // Add more diagnostic information
            Console.WriteLine($"Server IP addresses:");
            var addresses = Dns.GetHostAddresses(Dns.GetHostName())
                .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork);
            foreach (var ip in addresses)
            {
                Console.WriteLine($"  - {ip}");
            }

            try
            {
                listener.Start();
                Console.WriteLine($"Server started successfully on port {port}");

                while (true)
                {
                    Console.WriteLine("Waiting for new connection...");
                    using var client = await listener.AcceptTcpClientAsync();
                    var endpoint = client.Client.RemoteEndPoint as IPEndPoint;
                    Console.WriteLine($"New connection attempt from: {endpoint?.ToString() ?? "unknown"}");

                    // Rest of your code...
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Detailed server error: {ex.GetType().Name} - {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                listener.Stop();
            }
        }

        public static async Task RunClient(string serverAddress, int port)
        {
            try
            {
                Console.WriteLine($"Connecting to {serverAddress}:{port}...");
                using var client = new TcpClient();

                await client.ConnectAsync(serverAddress, port);
                Console.WriteLine("Connected to server!");

                using var stream = client.GetStream();
                using var reader = new StreamReader(stream);
                using var writer = new StreamWriter(stream) { AutoFlush = true };

                // Send test message
                var message = $"Test message from {Environment.MachineName} at {DateTime.Now}";
                await writer.WriteLineAsync(message);
                Console.WriteLine($"Sent: {message}");

                // Read response
                var response = await reader.ReadLineAsync();
                Console.WriteLine($"Received: {response}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Client error: {ex.Message}");
            }
        }
    }

    public class Program
    {
        public static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("  server <port>");
                Console.WriteLine("  client <server-address> <port>");
                return;
            }

            if (args[0].ToLower() == "server" && args.Length >= 2)
            {
                if (int.TryParse(args[1], out int port))
                {
                    await TcpTester.RunServer(port);
                }
            }
            else if (args[0].ToLower() == "client" && args.Length >= 3)
            {
                if (int.TryParse(args[2], out int port))
                {
                    await TcpTester.RunClient(args[1], port);
                }
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}