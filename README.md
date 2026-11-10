# MD Dia-Inch

A dockable panel for Navisworks Manage and Simulate 2017–2027. It reads piping models exported from AVEVA PDMS or E3D (NWD / NWF) and works out, for each pipeline:

- **Weld joints:** butt weld (BW) and socket weld (SW). Flange joints are counted in their own column. Screwed joints are counted separately and are left out of the total by default.
- **Inch-dia** (inch-diameter): the sum of the joint NB in inches, for every counted weld joint.
- **Pipe length (m)** and **inch-meter:** tube NB in inches × tube length in metres.
- **Totals:** per line, per area, per chosen hierarchy, per size, and a grand total.

A number is never invented. When a bore, length or connection type is missing, or was only assumed from the rules, the line is marked **CHECK** and the **Notes** column says why.

It can optionally import pipe parents you already saved in **MD Hierarchy Scan** (a separate tool). Dia-Inch works on its own if you do not have that tool.

**Author:** MD (Mayank Dholariya)  
**Version:** 1.0.0  
**License:** MIT

## Screenshots

_Add screenshots of the dock pane, a CHECK-highlighted grid, and a sample Excel report in `docs/images/`._

## Supported Navisworks versions

Autodesk Navisworks **Manage** and **Simulate** **2017–2027** (64-bit). Each year gets its own DLL. Navisworks Freedom is not supported.

## Install (no administrator rights)

Download the latest installer or portable zip from [Releases](../../releases). Close Navisworks first.

- **Setup:** run `MD_Navisworks_DiaInch_Setup_v1.0.0.exe`. It installs for the current Windows user only, to `%APPDATA%\Autodesk\ApplicationPlugins\MD.DiaInch.bundle`. No admin rights, no Program Files.
- **Portable:** unzip `MD_Navisworks_DiaInch_Portable_v1.0.0.zip` and run `install.cmd`. Use `check.cmd` and `uninstall.cmd` as needed.
- **Uninstall:** Windows Settings → Apps → "MD Dia-Inch".

Open the panel from **Tool add-ins 1 → MD Dia-Inch**.

## Use

1. Open the NWD / NWF.
2. Choose a **Scope**: scan the whole model, or limit to chosen hierarchies (select pipe parents in the Selection Tree, then **Add selected**).
3. Press **Scan**. The grid shows one row per line. Rows marked CHECK are highlighted.
4. Click a row to select that line in the model. Use **Show:** to switch between Lines, Area totals, Hierarchy totals and By size.
5. Press **Export Excel** or **Export CSV**. A sample report is in `docs/DiaInch_sample_demo.xlsx`.
6. If the results look wrong, select one branch or component and press **Inspect selection**, then adjust `%APPDATA%\MDDiaInch\rules.json` (defaults are in `docs/rules.default.json`) and **Reload rules**.

The add-in only reads the model. It writes nothing to the NWD / NWF.

The full joint-counting rules, attribute aliases, switches and known limits are in [docs/USE.md](docs/USE.md). The log is `%APPDATA%\MDDiaInch\mddiainch.log`.

## Build from source

You need the .NET SDK (8 or later is fine for tests; plugin builds target .NET Framework 4.6–4.8).

**Do not commit Autodesk Navisworks DLLs.** They are not redistributable. Point `NwRefDir` at your own Navisworks install (this plugin also needs `Autodesk.Navisworks.ComApi.dll` and `Autodesk.Navisworks.Interop.ComApi.dll` from the same folder).

```bash
# Core unit tests (no Navisworks required)
dotnet test tests/DiaInch.Tests/DiaInch.Tests.csproj -c Release

# Plugin DLL for one year, against your Navisworks install
dotnet build src/MDDiaInch/MDDiaInch.csproj -c Release \
  -p:NwYear=2024 \
  -p:NwRefDir="C:/Program Files/Autodesk/Navisworks Manage 2024" \
  -p:NwTfm=net48
```

`NwTfm` is `net46` for 2017–2018, `net47` for 2019–2020, and `net48` for 2021–2027.

To build every year, set `NW_REFS` to a directory of per-year API folders (`$NW_REFS/2017`, …) and run `./build_all.sh`. That script always runs the Core tests first.

The no-admin installer is compiled with [Inno Setup 6](https://jrsoftware.org/isinfo.php) from `installer/DiaInch_NoAdmin.iss`. Set the `ISCC` environment variable to `ISCC.exe` if needed. `python3 installer/make_release.py` stages the bundle, compiles the setup and builds the portable zip.

## Tests

```bash
dotnet test tests/DiaInch.Tests/DiaInch.Tests.csproj -c Release
```

The Core tests do not need Navisworks.

## License

MIT © 2026 MD (Mayank Dholariya). See [LICENSE](LICENSE).
