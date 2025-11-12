using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pfizer.EmpInfoUpdate.Model
{
    /// <summary>
    /// Request model for updating employee profile information
    /// </summary>
    public class UpdateEmployeeProfileRequest
    {
        /// <summary>
        /// Employee ID (required)
        /// </summary>
        [Required]
        [StringLength(64, MinimumLength = 1)]
        public required string employee_id { get; set; }

        /// <summary>
        /// New department name (optional)
        /// </summary>
        [StringLength(100)]
        public string? department { get; set; }

        /// <summary>
        /// New job title (optional)
        /// </summary>
        [StringLength(100)]
        public string? job_title { get; set; }

        /// <summary>
        /// New address (optional)
        /// </summary>
        [StringLength(250)]
        public string? address { get; set; }
    }
}
