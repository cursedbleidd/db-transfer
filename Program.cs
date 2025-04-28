using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Diagnostics;
using static System.Reflection.Metadata.BlobBuilder;

namespace db_transfer
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            //Console.WriteLine("Начало импорта данных в нормализованную БД");
            //await TransferData();
            //Console.WriteLine("Импорт прошел успешно");
            //Console.WriteLine("Введите имя читателя:");
            //var name = Console.ReadLine();
            //ArgumentNullException.ThrowIfNullOrWhiteSpace(name);
            //await ExportDataToExcel(name);
            //Console.ReadKey();
            //Console.WriteLine("Очистка базы данных...");
            //await ClearDB();

            string menu = """
                Выберите опцию:
                Socket
                1. Отправить
                2. Получить
                Rabbit
                3. Отправить
                4. Получить
                gRPC
                5. Отправить
                6. Получить

                7. Очистить Postgres
                8. Импорт в эксель
                0. Выход
                """;

            Console.WriteLine(menu);
            
            while (int.TryParse(Console.ReadLine(), out int choice) && choice != 0)
            {
                switch (choice) 
                {
                    case int i when i % 2 == 1 && i < 7: 
                        Client client = new();
                        switch (i)
                        {
                            case 1:
                                client.StartSocket();
                                break;
                            case 3:
                                client.StartRabbit();
                                break;
                            case 5:
                                await client.SendProjectsAsync();
                                break;
                        }
                        break;
                    case int i when i % 2 == 0 || i >= 7:
                        Server server = new();
                        switch (i)
                        {
                            case 2:
                                server.StartSocketServer();
                                break;
                            case 4:
                                server.ReadFromQueue();
                                break;
                            case 6:
                                await server.StartGrpcServerAsync();
                                break;
                            case 7:
                                server.TruncateTables();
                                break;
                            case 8:
                                server.RunPythonScript();
                                break;
                        }
                        break;
                }
                Console.ReadKey();
                Console.Clear();
                Console.WriteLine(menu);
            }
        }

        static async Task TransferData()
        {
            using var libraryContext = new LibraryContext();
            using var identifierContext = new IdentifierContext();

            foreach (var library in identifierContext.Libraries)
            {
                var borrow = new Borrow()
                {
                    BorrowDate = library.BorrowDate,
                    ReturnDate = library.ReturnDate,
                };

                
                var reader = await libraryContext.Readers.FirstOrDefaultAsync(r => r.Name == library.ReaderName);
                borrow.Reader = reader ?? new Reader() { Name = library.ReaderName };

                libraryContext.Readers.Add(new Reader());
                
                var book = await libraryContext.Books
                    .Include(b => b.Authors) 
                    .FirstOrDefaultAsync(b => b.Title == library.BookTitle && b.Genre == library.BookGenre);

                if (book != null)
                {
                    borrow.Book = book;

                    var author = book.Authors.FirstOrDefault(a => a.Name == library.AuthorName);
                    if (author == null)
                    {
                        
                        author = await libraryContext.Authors.FirstOrDefaultAsync(a => a.Name == library.AuthorName);
                        if (author == null)
                        {
                            
                            author = new Author() { Name = library.AuthorName };
                            await libraryContext.Authors.AddAsync(author);
                        }

                        book.Authors.Add(author);
                    }
                }
                else
                {
                    var author = await libraryContext.Authors.FirstOrDefaultAsync(a => a.Name == library.AuthorName);
                    if (author == null)
                    {
                        author = new Author() { Name = library.AuthorName };
                        await libraryContext.Authors.AddAsync(author);
                    }

                    borrow.Book = new Book()
                    {
                        Title = library.BookTitle,
                        Genre = library.BookGenre,
                        Authors = new List<Author> { author }
                    };
                }
                var existingBorrow = await libraryContext.Borrows.FirstOrDefaultAsync(
                    a => a.BookId == borrow.Book.BookId &&
                    a.ReaderId == borrow.Reader.ReaderId &&
                    a.BorrowDate == borrow.BorrowDate &&
                    a.ReturnDate == borrow.ReturnDate);
                if (existingBorrow == null)
                    libraryContext.Borrows.Add(borrow);

                await libraryContext.SaveChangesAsync();
                
            }
        }
        static async Task ClearDB()
        {
            using var libraryContext = new LibraryContext();

            libraryContext.Borrows.RemoveRange(libraryContext.Borrows);
            libraryContext.Readers.RemoveRange(libraryContext.Readers);
            libraryContext.Books.RemoveRange(libraryContext.Books);
            libraryContext.Authors.RemoveRange(libraryContext.Authors);

            await libraryContext.SaveChangesAsync();

            await libraryContext.Database.ExecuteSqlRawAsync("ALTER SEQUENCE \"borrows_borrow_id_seq\" RESTART WITH 1;");
            await libraryContext.Database.ExecuteSqlRawAsync("ALTER SEQUENCE \"readers_reader_id_seq\" RESTART WITH 1;");
            await libraryContext.Database.ExecuteSqlRawAsync("ALTER SEQUENCE \"books_book_id_seq\" RESTART WITH 1;");
            await libraryContext.Database.ExecuteSqlRawAsync("ALTER SEQUENCE \"authors_author_id_seq\" RESTART WITH 1;");
        }
        static async Task ExportDataToExcel(string name)
        {
            using var libraryContext = new LibraryContext();

            var data = await libraryContext.Borrows
                .Where(b => b.Reader.Name == name)
                .Select(b => new
                {
                    bookTitle = b.Book.Title,
                    authors = string.Join(", ", b.Book.Authors.Select(a => a.Name)),
                    borrowDate = b.BorrowDate.ToString("yyyy-MM-dd"),
                    returnDate = b.ReturnDate.HasValue ? b.ReturnDate.Value.ToString("yyyy-MM-dd") : null,
                })
            .ToListAsync();

            string json = JsonSerializer.Serialize(data,
                new JsonSerializerOptions
                { 
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

            string tempFile = Path.GetTempFileName();
            await File.WriteAllTextAsync(tempFile, json);

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"export_to_excel.py \"{tempFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,    
                CreateNoWindow = true
            };

            using Process? process = Process.Start(psi);

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            process?.WaitForExit();

            File.Delete(tempFile);

            Console.WriteLine(output);
            if (!string.IsNullOrEmpty(error))
                Console.WriteLine(error);
            Console.WriteLine("Данные экспортированы в Excel.");
        }
    }
}
