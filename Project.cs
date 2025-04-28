using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Protos;

namespace db_transfer
{
    public class Project
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string ProjectName { get; set; }
        public DateTime ProjectStartDate { get; set; }
        public DateTime ProjectEndDate { get; set; }
        public string ProjectStatus { get; set; }
        public string TaskName { get; set; }
        public string ExecutorName { get; set; }
        public string TaskStatus { get; set; }
        public DateTime TaskDueDate { get; set; }
        public string TeamName { get; set; }


        public Project(string firstName, string lastName, string email, string projectName,
                           DateTime projectStartDate, DateTime projectEndDate, string projectStatus,
                           string taskName, string executorName, string taskStatus,
                           DateTime taskDueDate, string teamName)
        {
            FirstName = firstName;
            LastName = lastName;
            Email = email;
            ProjectName = projectName;
            ProjectStartDate = projectStartDate;
            ProjectEndDate = projectEndDate;
            ProjectStatus = projectStatus;
            TaskName = taskName;
            ExecutorName = executorName;
            TaskStatus = taskStatus;
            TaskDueDate = taskDueDate;
            TeamName = teamName;
        }

        public Project ()
        {

        }

        public Project(ProjectRequest request)
        {
            FirstName = request.FirstName;
            LastName = request.LastName;
            Email = request.Email;
            ProjectName = request.ProjectName;
            ProjectStartDate = DateTimeOffset.FromUnixTimeSeconds(request.ProjectStartDate).UtcDateTime;
            ProjectEndDate = DateTimeOffset.FromUnixTimeSeconds(request.ProjectEndDate).UtcDateTime;
            ProjectStatus = request.ProjectStatus;
            TaskName = request.TaskName;
            ExecutorName = request.ExecutorName;
            TaskStatus = request.TaskStatus;
            TaskDueDate = DateTimeOffset.FromUnixTimeSeconds(request.TaskDueDate).UtcDateTime;
            TeamName = request.TeamName;
        }
    }

}
