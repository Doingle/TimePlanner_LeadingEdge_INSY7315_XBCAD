using System;
using System.Collections.Generic;
using System.Text;

namespace TimePlanner.Core.Domain.Entities
{
    //-----------------------------
    //this class represents a company which owns a project, connected to a task within TimePlanner
    public class Company
    {
        public int CompanyId { get; set; }

        //set the company name as empty avoiding null references
        public string Name { get; set; } = string.Empty;

        public ICollection<Project> Projects { get; set; } = new List<Project>();

        //-----------------------------
        //this reporting method calculates an aggregate of all logged time for a specific company accross all their projects
        public TimeSpan GetTotalDuration()
        {
            var total = TimeSpan.Zero;

            foreach (var project in Projects)
            {
                foreach (var task in project.Tasks) {
                    total += task.GetTotalLoggedTime();
                }
            }
            return total;
        }
    }

}
//------------------------------EOF-----------------------------\\