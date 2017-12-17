﻿using MaiReo.Nuget.Server.Models;
using MaiReo.Nuget.Server.Tools;
using Microsoft.Extensions.Options;
using System.Collections.Generic;

namespace MaiReo.Nuget.Server.Core
{
    public class ZipFileNuspecProvider : INuspecProvider
    {
        public ZipFileNuspecProvider(
            IOptions<NugetServerOptions> nugetServerOptionsAccessor)
        {
            this.NugetServerOptions = nugetServerOptionsAccessor.Value;
        }

        public NugetServerOptions NugetServerOptions { get; private set; }

        public virtual IEnumerable<Nuspec> GetAll()
        {
            foreach (var filePath in this.GetAllPackagePaths())
            {
                var nuspec = Zip.ReadNuspec(filePath);
                nuspec.FilePath = filePath;
                yield return nuspec;
            }
        }
    }
}
