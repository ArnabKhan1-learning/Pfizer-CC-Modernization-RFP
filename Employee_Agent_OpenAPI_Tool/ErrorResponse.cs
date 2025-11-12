using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace Pfizer.EmpInfoUpdate.Model
{
    /// <summary>
    /// Standard error response model for API operations
    /// </summary>
    public class ErrorResponse
    {
        /// <summary>
        /// Human-readable error message
        /// </summary>
        [Required]
        [StringLength(500)]
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Error code for programmatic error handling
        /// </summary>
        public int ErrorCode { get; set; }
    }
}