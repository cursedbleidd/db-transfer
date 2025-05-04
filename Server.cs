using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System;
using System.Threading.Tasks;
using Protos;

namespace db_transfer
{
    public class Server
    {
        private Process? pythonProcess;

        public Server()
        {
        }

        public void RunPythonScript()
        {
            PostgreSQL postgreSQL = new PostgreSQL();

            string exeDirectory = AppContext.BaseDirectory;
            string scriptPath = Path.Combine(exeDirectory, "script.py");
            string outJson = Path.Combine(exeDirectory, "output.json");

            postgreSQL.ExportDataToJson(outJson);

            if (!File.Exists(scriptPath))
            {
                Console.WriteLine($"Файл {scriptPath} не найден.");
                return;
            }

            string pythonPath = FindPythonPath();
            if (string.IsNullOrEmpty(pythonPath))
            {
                Console.WriteLine("Python не найден.");
                return;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/K \"{pythonPath} \"{scriptPath}\"\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = exeDirectory
            };

            pythonProcess = Process.Start(startInfo);
            pythonProcess?.WaitForExit();
        }

        public void StopPythonProcess()
        {
            if (pythonProcess != null && !pythonProcess.HasExited)
            {
                pythonProcess.Kill();
                pythonProcess.Dispose();
                pythonProcess = null;
                Console.WriteLine("Python процесс остановлен.");
            }
        }

        public static string FindPythonPath()
        {
            try
            {
                using Process process = new Process();
                process.StartInfo.FileName = "where";
                process.StartInfo.Arguments = "python";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                string[] paths = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return paths.Length > 0 ? paths[0].Trim() : null!;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при поиске Python: {ex.Message}");
                return null!;
            }
        }

        public void ReadFromQueue()
        {
            var sslOptions = new SslOption
            {
                Enabled = true,
                ServerName = "localhost",
                CertPath = "C:/Certs/ServerCertOnly.pem",
                CertPassphrase = "key123456"
            };
            var factory = new ConnectionFactory
            {
                HostName = "localhost",
                Port = 5671,
                UserName = "guest",
                Password = "guest",
                Ssl = sslOptions
            };

            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            channel.QueueDeclare(queue: "test_queue1",
                                 durable: false,
                                 exclusive: false,
                                 autoDelete: true,
                                 arguments: null);

            while (true)
            {
                var result = channel.BasicGet(queue: "test_queue1", autoAck: false);
                if (result == null)
                {
                    Console.WriteLine("Очередь пуста. Чтение завершено.");
                    break;
                }

                try
                {
                    var body = result.Body.ToArray();
                    var json = Encoding.UTF8.GetString(body);
                    Project project = JsonConvert.DeserializeObject<Project>(json)!;

                    PostgreSQL postgre = new PostgreSQL();
                    postgre.Migrate(project);

                    channel.BasicAck(deliveryTag: result.DeliveryTag, multiple: false);

                    Console.WriteLine($"Получен проект: {project.ProjectName}");
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Ошибка десериализации: {ex.Message}");
                    channel.BasicNack(deliveryTag: result.DeliveryTag, multiple: false, requeue: false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при обработке сообщения: {ex.Message}");
                    channel.BasicNack(deliveryTag: result.DeliveryTag, multiple: false, requeue: true);
                }
            }

            Console.WriteLine("Данные успешно получены!");
        }

        public void StartSocketServer()
        {
            TcpListener? server = null;
            try
            {
                int port = 13000;
                IPAddress localAddr = IPAddress.Any;
                server = new TcpListener(localAddr, port);
                server.Start();

                Console.WriteLine("Сервер запущен. Ожидание подключения...");

                using TcpClient client = server.AcceptTcpClient();
                Console.WriteLine("Клиент подключен.");

                using SslStream sslStream = new SslStream(client.GetStream(), false);
                X509Certificate2 serverCertificate = new X509Certificate2("C:\\Certs\\ServerCert.pfx", "key123456");

                sslStream.AuthenticateAsServer(serverCertificate, clientCertificateRequired: false, SslProtocols.Tls12, checkCertificateRevocation: true);

                using StreamReader reader = new StreamReader(sslStream, Encoding.UTF8);
                string? line;
                Console.WriteLine("Получение данных от клиента:");
                PostgreSQL postgre = new PostgreSQL();
                while (true)
                {
                    byte[] lengthBytes = new byte[4];
                    int bytesRead = sslStream.Read(lengthBytes, 0, lengthBytes.Length);
                    if (bytesRead == 0) break;

                    int messageLength = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes, 0));

                    byte[] buffer = new byte[messageLength];
                    int totalBytesRead = 0;
                    while (totalBytesRead < messageLength)
                    {
                        int bytesToRead = Math.Min(42, messageLength - totalBytesRead);
                        bytesRead = sslStream.Read(buffer, totalBytesRead, bytesToRead);
                        if (bytesRead == 0) break;

                        totalBytesRead += bytesRead;
                    }

                    string json = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                    try
                    {
                        Project project = JsonConvert.DeserializeObject<Project>(json);
                        postgre.Migrate(project);
                        Console.WriteLine($"Получен проект: {project.ProjectName}");
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"Ошибка десериализации: {ex.Message}");
                    }
                }

                Console.WriteLine("Соединение закрыто.");
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Ошибка сокета: {ex.Message}");
            }
            finally
            {
                server?.Stop();
            }
        }

        public void TruncateTables()
        {
            PostgreSQL postgre = new PostgreSQL();
            postgre.TruncateTables();
            Console.WriteLine("Таблицы очищены.");
        }

        public async Task StartGrpcServerAsync()
        {
            var certPath = Path.Combine(AppContext.BaseDirectory, "Certificates", "server.pfx");
            var certPassword = "key123";

            try
            {
                var builder = WebApplication.CreateBuilder();

                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.ListenAnyIP(50051, listenOptions =>
                    {
                        var serverCert = new X509Certificate2(certPath, certPassword);
                        listenOptions.UseHttps(serverCert);
                    });
                });

                builder.Services.AddGrpc();

                var app = builder.Build();

                app.MapGrpcService<MyProjectService>();

                await app.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось запустить сервер GRPC: {ex.Message}");
            }
        }
    }
}
