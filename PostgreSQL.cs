using Newtonsoft.Json;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace db_transfer
{
    internal class PostgreSQL
    {
        private const string PostgresConnectionString = "Host=localhost;Port=5432;Username=postgres;Password=test;Database=Projects";

        public void ExportDataToJson(string outputPath)
        {

            string query = @"
    WITH executor AS (
        SELECT t.assigned_to AS executor_id,
               e.first_name || ' ' || e.last_name || ', email: ' || e.email AS executor_name
          FROM task t
          JOIN employee e ON t.assigned_to = e.employee_id 
    ),
    link_project AS (
        SELECT t.project_id,
               p.project_name || ' status: ' || p.status AS task_link_project
          FROM task t 
          JOIN project p ON t.project_id = p.project_id
    ),
    leader AS (
        SELECT t.team_id, 
               'leader: ' || e.first_name || ' ' || e.last_name || ', email: ' || e.email AS team_leader
          FROM team t
          JOIN employee e ON t.lead_employee_id = e.employee_id 
    )
    SELECT e.first_name,
           e.last_name,
           e.email,
           p.project_name,
           p.start_date AS project_start_date,
           p.end_date AS project_end_date,
           p.status AS project_status,
           t.task_name,
           ex.executor_name,
           lp.task_link_project,
           t.status AS task_status,
           t.due_date AS task_due_date,
           t2.team_name,
           l.team_leader
      FROM employee e
      LEFT JOIN task t ON t.assigned_to = e.employee_id 
      LEFT JOIN project p ON p.project_id = t.project_id 
      LEFT JOIN executor ex ON ex.executor_id = t.assigned_to
      LEFT JOIN link_project lp ON lp.project_id = t.project_id
      LEFT JOIN project_team pt ON pt.project_id = p.project_id
      LEFT JOIN team t2 ON t2.team_id = pt.team_id
      LEFT JOIN leader l ON l.team_id = t2.team_id";

            string finalQuery = $"SELECT * FROM ({query}) AS subquery;";
            string[] columns = new[]
            {
                "first_name",
                "last_name",
                "email",
                "project_name",
                "project_start_date",
                "project_end_date",
                "project_status",
                "task_name",
                "executor_name",
                "task_status",
                "task_due_date",
                "team_name"
            };

            using (var connection = new NpgsqlConnection(PostgresConnectionString))
            {
                connection.Open();

                using (var command = new NpgsqlCommand(finalQuery, connection))
                using (var reader = command.ExecuteReader())
                {
                    var dataList = new List<Dictionary<string, object>>();
                    reader.NextResult();
                    while (reader.Read())
                    {
                        var row = new Dictionary<string, object>();
                        foreach (string column in columns)
                        {
                            row[column] = reader[column];
                        }
                        dataList.Add(row);
                    }

                    string jsonOutput = JsonConvert.SerializeObject(dataList, Formatting.Indented);

                    File.WriteAllText(outputPath, jsonOutput);
                }
            }

            Console.WriteLine($"Данные успешно экспортированы в {outputPath}");
        }

        public void Migrate(Project project)
        {
            using (var pgConnection = new NpgsqlConnection(PostgresConnectionString))
            {
                pgConnection.Open();

                using (var pgTransaction = pgConnection.BeginTransaction())
                {
                    using (var employeeCommand = pgConnection.CreateCommand())
                    using (var teamCommand = pgConnection.CreateCommand())
                    using (var projectCommand = pgConnection.CreateCommand())
                    using (var taskCommand = pgConnection.CreateCommand())
                    {
                        // Словари для айдишников
                        var employeeIds = new Dictionary<string, int>();
                        var teamIds = new Dictionary<string, int>();
                        var projectIds = new Dictionary<string, int>();

                        // 1. Вставка или получение айди сотрудника
                        if (!employeeIds.ContainsKey(project.Email))
                        {
                            employeeCommand.CommandText = "INSERT INTO employee (first_name, last_name, email) VALUES (@firstName, @lastName, @Email) ON CONFLICT (email) DO NOTHING RETURNING employee_id";
                            employeeCommand.Parameters.Clear();
                            employeeCommand.Parameters.AddWithValue("@firstName", project.FirstName);
                            employeeCommand.Parameters.AddWithValue("@lastName", project.LastName);
                            employeeCommand.Parameters.AddWithValue("@Email", project.Email);

                            var result = employeeCommand.ExecuteScalar();
                            int employeeId;
                            if (result != null)
                            {
                                employeeId = (int)result;
                            }
                            else
                            {
                                employeeCommand.CommandText = "SELECT employee_id FROM employee WHERE email = @Email";
                                employeeId = (int)employeeCommand.ExecuteScalar();
                            }
                            employeeIds[project.Email] = employeeId;
                        }

                        int assignedToId = employeeIds[project.Email];

                        // 2. Вставка или получение айди команды
                        if (!teamIds.ContainsKey(project.TeamName))
                        {
                            teamCommand.CommandText = "INSERT INTO team (team_name, lead_employee_id) VALUES (@teamName, @leadEmployeeId) ON CONFLICT (team_name, lead_employee_id) DO NOTHING RETURNING team_id";
                            teamCommand.Parameters.Clear();
                            teamCommand.Parameters.AddWithValue("@teamName", project.TeamName);
                            teamCommand.Parameters.AddWithValue("@leadEmployeeId", assignedToId);

                            var result = teamCommand.ExecuteScalar();
                            int teamId;
                            if (result != null)
                            {
                                teamId = (int)result;
                            }
                            else
                            {
                                teamCommand.CommandText = "SELECT team_id FROM team WHERE team_name = @teamName AND lead_employee_id = @leadEmployeeId";
                                teamId = (int)teamCommand.ExecuteScalar();
                            }
                            teamIds[project.TeamName] = teamId;
                        }

                        int team_id = teamIds[project.TeamName];

                        // 3. Вставка или получение айди проекта
                        if (!projectIds.ContainsKey(project.ProjectName))
                        {
                            projectCommand.CommandText = "INSERT INTO project (project_name, start_date, end_date, status) VALUES (@projectName, @startDate, @endDate, @status) ON CONFLICT (project_name, status) DO NOTHING RETURNING project_id";
                            projectCommand.Parameters.Clear();
                            projectCommand.Parameters.AddWithValue("@projectName", project.ProjectName);
                            projectCommand.Parameters.AddWithValue("@startDate", project.ProjectStartDate);
                            projectCommand.Parameters.AddWithValue("@endDate", project.ProjectEndDate);
                            projectCommand.Parameters.AddWithValue("@status", project.ProjectStatus);


                            var result = projectCommand.ExecuteScalar();
                            int projectId;
                            if (result != null)
                            {
                                projectId = (int)result;
                            }
                            else
                            {
                                projectCommand.CommandText = "SELECT project_id FROM project WHERE project_name = @projectName AND status = @status";
                                projectId = (int)projectCommand.ExecuteScalar();
                            }
                            projectIds[project.ProjectName] = projectId;
                        }

                        int project_id = projectIds[project.ProjectName];

                        // 4. Вставка задачи
                        taskCommand.CommandText = "INSERT INTO task (task_name, assigned_to, project_id, status, due_date) VALUES (@taskName, @assignedTo, @projectId, @status, @dueDate) ON CONFLICT (task_name, assigned_to, project_id, status) DO NOTHING";
                        taskCommand.Parameters.Clear();
                        taskCommand.Parameters.AddWithValue("@taskName", project.TaskName);
                        taskCommand.Parameters.AddWithValue("@assignedTo", assignedToId);
                        taskCommand.Parameters.AddWithValue("@projectId", project_id);
                        taskCommand.Parameters.AddWithValue("@status", project.TaskStatus);
                        taskCommand.Parameters.AddWithValue("@dueDate", project.TaskDueDate);

                        taskCommand.ExecuteNonQuery();

                        // связываем проект и команду
                        using (var projectTeamCommand = pgConnection.CreateCommand())
                        {
                            projectTeamCommand.CommandText = "INSERT INTO project_team (project_id, team_id) VALUES (@projectId, @teamId) ON CONFLICT (project_id, team_id) DO NOTHING";
                            projectTeamCommand.Parameters.Clear();
                            projectTeamCommand.Parameters.AddWithValue("@projectId", project_id);
                            projectTeamCommand.Parameters.AddWithValue("@teamId", team_id);
                            projectTeamCommand.ExecuteNonQuery();
                        }

                    }

                    pgTransaction.Commit();
                }
            }
        }

        public void TruncateTables()
        {
            // Список таблиц для очистки
            string[] tablesToTruncate = {
            "public.employee",
            "public.project",
            "public.project_team",
            "public.task",
            "public.team"
            };

            try
            {
                // Подключение к базе данных
                using (var connection = new NpgsqlConnection(PostgresConnectionString))
                {
                    connection.Open();
                    Console.WriteLine("Подключение к базе данных успешно.");

                    // Создаем команду для выполнения SQL-запросов
                    using (var command = new NpgsqlCommand())
                    {
                        command.Connection = connection;

                        foreach (var table in tablesToTruncate)
                        {
                            // Формируем SQL-запрос для очистки таблицы
                            string truncateTableQuery = $"TRUNCATE TABLE {table} CASCADE;";
                            command.CommandText = truncateTableQuery;

                            // Выполняем запрос
                            command.ExecuteNonQuery();
                            Console.WriteLine($"Таблица {table} успешно очищена.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Обработка ошибок
                Console.WriteLine($"Ошибка при очистке таблиц: {ex.Message}");
            }
        }
    }
}
