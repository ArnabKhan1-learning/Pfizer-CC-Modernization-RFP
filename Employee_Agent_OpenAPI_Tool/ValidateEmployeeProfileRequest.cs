using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pfizer.EmpInfoUpdate.Model
{
    /// <summary>
    /// Request model for validating employee profile identity information
    /// </summary>
    public class ValidateEmployeeProfileRequest
    {
        /// <summary>
        /// Employee ID (required)
        /// </summary>
        [Required]
        [StringLength(64, MinimumLength = 1)]
        public required string employee_id { get; set; }

        /// <summary>
        /// Employee first name (required)
        /// </summary>
        [Required]
        [StringLength(100, MinimumLength = 1)]
        public required string first_name { get; set; }

        /// <summary>
        /// Employee last name (required)
        /// </summary>
        [Required]
        [StringLength(100, MinimumLength = 1)]
        public required string last_name { get; set; }
    }
}
