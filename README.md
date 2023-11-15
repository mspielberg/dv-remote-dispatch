##### Credits

Icons made by [Freepik](https://www.freepik.com) from [Flaticon](https://www.flaticon.com/).


### Contributing

To build this project, first import it into visual studio.  You'll need to make a new file called Directory.Build.targets to specify file paths.

In addition, if properly configured, this file will automatically drop the output of the build into your derail valley mods folder.  UMM is still required to exist, but the info.json and dll will be enough to continue to develop.

An example of what the targets file should look like is provided:

```
<Project>
  <!-- See https://aka.ms/dotnet/msbuild/customize for more details on customizing your build -->
  <PropertyGroup>
    <ReferencePath>
      C:\Program Files (x86)\Steam\steamapps\common\Derail Valley\DerailValley_Data\Managed\
    </ReferencePath>
    <AssemblySearchPaths>$(AssemblySearchPaths);$(ReferencePath);</AssemblySearchPaths>
  </PropertyGroup>
  <Target Name="AfterBuild">
    <Exec Command="powershell -executionPolicy bypass .\package.ps1 -NoArchive -OutputDirectory &quot;\&quot;c:/Program Files (x86)/Steam/steamapps/common/Derail Valley/Mods\&quot;&quot;" />
  </Target>
</Project>
```

This example assumes that you are building on Windows - if you are using linux, you'll need to use appropriate linux paths and will need to use "pwsh" to run the package script.