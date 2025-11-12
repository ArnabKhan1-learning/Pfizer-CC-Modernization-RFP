using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace Pfizer.EmpInfoUpdate.Model
{
    /// <summary>
    /// Response model for employee profile update operations
    /// </summary>
    public class UpdateEmployeeResponse
    {
        /// <summary>
        /// Update operation result message
        /// </summary>
        [Required]
        [StringLength(250)]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Number of rows updated in the database
        /// </summary>
        [Range(0, int.MaxValue)]
        public int RowsUpdated { get; set; }
    }
}
