using Grpc.Net.Client;
using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Protos;

namespace db_transfer
{
    internal class Client
    {
        string deviceName = "DESKTOP-B0RNFQA";
        int port = 13000;

        public Client(string deviceName, int port)
        {
            this.deviceName = deviceName;
            this.port = port;
        }
        public static string GetIPv4AddressByNetworkName(string networkName = "Беспроводная сеть")
        {
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var networkInterface in networkInterfaces)
            {
                if ((networkInterface.Name.Equals(networkName, StringComparison.OrdinalIgnoreCase) ||
                     networkInterface.Description.Equals(networkName, StringComparison.OrdinalIgnoreCase)) &&
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    var ipProperties = networkInterface.GetIPProperties();

                    var ipv4Address = ipProperties.UnicastAddresses
                        .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(addr => addr.Address.ToString())
                        .FirstOrDefault();

                    if (!string.IsNullOrEmpty(ipv4Address))
                    {
                        return ipv4Address;
                    }
                }
            }

            throw new Exception($"Не удалось найти IPv4-адрес для сети с именем '{networkName}'.");
        }

        public void StartSocket()
        {
            IPHostEntry hostEntry = Dns.GetHostEntry(deviceName);

            IPAddress deviceIp = null;
            foreach (var address in hostEntry.AddressList)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    deviceIp = address;
                    break;
                }
            }
            Console.WriteLine($"Используем IP-адрес: {deviceIp}");

            using (TcpClient client = new TcpClient(deviceIp.ToString(), port))
            {
                Console.WriteLine("Подключение к серверу...");

                using (SslStream sslStream = new SslStream(client.GetStream(), false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null))
                {
                    X509Certificate2 caCertificate = new X509Certificate2("C:\\Certs\\RootCA.cer");

                    sslStream.AuthenticateAsClient("localhost"); // имя сервера

                    using IdentifierContext identifierContext = new IdentifierContext();

                    using (StreamWriter writer = new StreamWriter(sslStream, Encoding.UTF8) { AutoFlush = true })
                    {
                        Console.WriteLine("Отправка данных...");
                        foreach (var librrary in identifierContext.Libraries)
                        {
                            string json = JsonConvert.SerializeObject(librrary);
                            byte[] data = Encoding.UTF8.GetBytes(json);

                            // Отправка длины сообщения (4 байта)
                            byte[] lengthBytes = BitConverter.GetBytes(data.Length);
                            sslStream.Write(lengthBytes, 0, lengthBytes.Length);

                            // Разбиение данных на фрагменты по 42 байта
                            int chunkSize = 42;
                            for (int i = 0; i < data.Length; i += chunkSize)
                            {
                                int bytesToSend = Math.Min(chunkSize, data.Length - i);
                                sslStream.Write(data, i, bytesToSend);
                            }
                        }
                    }
                    Console.WriteLine("Данные отправлены.");
                }
            }
        }

        public void StartRabbit()
        {   
            IPHostEntry hostEntry = Dns.GetHostEntry(deviceName);
            IPAddress deviceIp = null!;
            foreach (var address in hostEntry.AddressList)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    deviceIp = address;
                    break;
                }
            }
            Console.WriteLine($"Используем IP-адрес: {deviceIp}");
            string rabbitmqHost = deviceIp.ToString();

            var sslOptions = new SslOption
            {
                Enabled = true,
                ServerName = "localhost"
            };
            var factory = new ConnectionFactory()
            {
                HostName = rabbitmqHost,
                Port = 5671,
                UserName = "user1",
                Password = "user1",
                Ssl = sslOptions
            };

            using (var connection = factory.CreateConnection())
            {
                using (var channel = connection.CreateModel())
                {
                    channel.QueueDeclare(queue: "test_queue1",
                                         durable: false,
                                         exclusive: false,
                                         autoDelete: true,
                                         arguments: null);
                    using IdentifierContext identifierContext = new IdentifierContext();

                    foreach (var project in identifierContext.Libraries)
                    {
                        string json = JsonConvert.SerializeObject(project);
                        byte[] data = Encoding.UTF8.GetBytes(json);

                        channel.BasicPublish(
                            exchange: "",
                            routingKey: "test_queue1",
                            basicProperties: null,
                            body: data
                        );
                    }
                    Console.WriteLine("Успешно отправлено!");
                }
            }
        }

        public async Task SendProjectsAsync(List<Library> libraries)
        {
            IPHostEntry hostEntry = Dns.GetHostEntry(deviceName);
            IPAddress deviceIp = null;
            foreach (var address in hostEntry.AddressList)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    deviceIp = address;
                    break;
                }
            }
            Console.WriteLine($"Используем IP-адрес: {deviceIp}");


            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            using var channel = GrpcChannel.ForAddress($"https://{deviceIp}:50051", new GrpcChannelOptions
            {
                HttpHandler = handler
            });

            var client = new LibraryService.LibraryServiceClient(channel);

            try
            {
                using var call = client.CreateLibrary();

                foreach (var library in libraries)
                {
                    var libraryRequest = new LibraryRequest
                    {
                        BookTitle = library.BookTitle,
                        BookGenre = library.BookGenre,
                        AuthorName = library.AuthorName,
                        ReaderName = library.ReaderName,
                        BorrowDate = new DateTimeOffset(library.BorrowDate.ToDateTime(TimeOnly.Parse("00:00"))).ToUnixTimeSeconds(),
                    };
                    if (library.ReturnDate.HasValue)
                        libraryRequest.ReturnDate = new DateTimeOffset(library.ReturnDate.Value.ToDateTime(TimeOnly.Parse("00:00"))).ToUnixTimeSeconds();
                    await call.RequestStream.WriteAsync(libraryRequest);
                    Console.WriteLine($"Отправлена запись: {library}");
                }
                await call.RequestStream.CompleteAsync();
                var response = await call.ResponseAsync;
                Console.WriteLine($"Ответ сервера:: {response.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
        }

        static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            Console.WriteLine($"Ошибка проверки сертификата: {sslPolicyErrors}");
            return false;
        }
    }
}

