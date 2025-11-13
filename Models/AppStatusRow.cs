using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StartUp
{
    public sealed class AppStatusRow
    {
        public string Name { get; init; } = "";      // plain
        public string Desktop { get; init; } = "";   // plain
        public string Status { get; set; } = "Pending"; // plain keyword we’ll color at render
        public string Details { get; set; } = "";    // plain, we’ll color grey at render
    }
}
