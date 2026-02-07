using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;

namespace StartUp.Services.Abstractions;

public interface ILog
{
    void Info(string message, object? context = null);
    void Warn(string message, object? context = null);
    void Error(Exception ex, string message, object? context = null);
}
