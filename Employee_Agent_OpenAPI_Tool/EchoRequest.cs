using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pfizer.EmpInfoUpdate.Model
{
    /// <summary>
    /// Request model for echo testing endpoint
    /// </summary>
    public class EchoRequest
    {
        /// <summary>
        /// Test name field
        /// </summary>
        [StringLength(50)]
        public string? Name { get; set; }

        /// <summary>
        /// Test integer value field
        /// </summary>
        [Range(-1000000, 1000000)]
        public int? Value { get; set; }

        /// <summary>
        /// Test description field
        /// </summary>
        [StringLength(250)]
        public string? Description { get; set; }
    }
}
