using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pfizer.EmpInfoUpdate.Model
{
    /// <summary>
    /// Request model for health check with optional extended diagnostics
    /// </summary>
    public class HealthCheckRequest
    {
        /// <summary>
        /// Whether to include database connectivity check
        /// </summary>
        public bool CheckDatabase { get; set; }

        /// <summary>
        /// Whether to include performance metrics
        /// </summary>
        public bool IncludeMetrics { get; set; }

        /// <summary>
        /// Optional client identifier for tracking
        /// </summary>
        [StringLength(100)]
        public string? ClientIdentifier { get; set; }
    }
}