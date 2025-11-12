using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace Pfizer.EmpInfoUpdate.Model
{
    /// <summary>
    /// Response model for employee profile validation
    /// </summary>
    public class ValidateEmployeeResponse
    {
        /// <summary>
        /// Indicates whether the validation was successful
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Validation result message
        /// </summary>
        [Required]
        [StringLength(250)]
        public string ValidationMessage { get; set; } = string.Empty;
    }
}
