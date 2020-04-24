using System.Collections.Generic;

namespace AmsHighAvailability.Models
{
    public class CompletedJob
    {
        public string AmsInstanceId { get; set; }

        public IEnumerable<string> OutputLabels { get; set; }
    }
}
