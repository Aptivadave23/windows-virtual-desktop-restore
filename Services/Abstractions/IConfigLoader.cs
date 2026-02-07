using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using StartUp;

namespace StartUp.Services.Abstractions;

public interface IConfigLoader
{
    WorkspaceConfig Load(string path);
}
