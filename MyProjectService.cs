using Grpc.Core;
using System.Collections.Generic;
using System.Threading.Tasks;
using Protos;

namespace db_transfer
{
    public class MyProjectService : ProjectService.ProjectServiceBase
    {
        public override async Task<ProjectResponse> CreateProject(IAsyncStreamReader<ProjectRequest> requestStream, ServerCallContext context)
        {
            var receivedProjects = new List<ProjectRequest>();

            try
            {
                PostgreSQL postgres = new PostgreSQL();
                while (await requestStream.MoveNext())
                {
                    var project = requestStream.Current;
                    receivedProjects.Add(project);

                    var saveproject = new Project(project);
                    postgres.Migrate(saveproject);

                    Console.WriteLine($"Received project: {project.ProjectName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while processing projects: {ex.Message}");
                return new ProjectResponse
                {
                    Message = "An error occurred while processing projects."
                };
            }

            Console.WriteLine($"Total projects received: {receivedProjects.Count}");

            return new ProjectResponse
            {
                Message = $"Successfully processed {receivedProjects.Count} projects."
            };
        }
    }
}