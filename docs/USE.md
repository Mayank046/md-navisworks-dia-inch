# MD Dia-Inch v1.0.0 for Navisworks

Author: MD (Mayank Dholariya)

MD Dia-Inch is a dockable panel for Navisworks Manage and Simulate 2017 to 2027. It reads piping models exported from AVEVA PDMS or E3D (NWD / NWF) and works out these figures for each pipeline:

* **Weld joints**: butt weld (BW) and socket weld (SW). Flange joints are counted in their own column. Screwed joints are counted separately and are left out of the total by default.
* **Inch-dia** (inch-diameter): the sum of the joint NB in inches, for every counted weld joint.
* **Pipe length (m)** and **inch-meter**: tube NB in inches × tube length in metres.
* **Totals**: per line, per area, per chosen hierarchy, per size, and a grand total.

It is a companion to **MD Hierarchy Scan**. It uses the same "Limit to chosen hierarchies" idea (several pipe parents can be chosen), and it can import the pipe parents you already saved in Hierarchy Scan.

> **Important: a number is never invented.** When a bore, length or connection type is missing, or was only assumed from the rules, the line is marked **CHECK** and the **Notes** column says why.

---

## Install (no admin rights)

The add-in installs for the current Windows user only, at:

`%APPDATA%\Autodesk\ApplicationPlugins\MD.DiaInch.bundle\`

Navisworks 2017 to 2027 loads it from there automatically. One DLL is shipped for each version, and `PackageContents.xml` picks the right one.

* **Setup EXE:** run `MD_Navisworks_DiaInch_Setup_v1.0.0.exe`. It does not ask for admin rights. To remove it, go to Windows Settings > Apps > "MD Dia-Inch".
* **Portable zip:** extract the whole zip, then double-click one of these:
  * `install.cmd` installs or updates the add-in.
  * `uninstall.cmd` removes it.
  * `check.cmd` shows which Navisworks versions were found and which add-in version is installed.

Close Navisworks before you install or uninstall. After installing, open the panel from **Tool add-ins 1 → MD Dia-Inch**.

The add-in does **not** use the `Navisworks\Plugins` folder. Earlier MD add-ins are not touched.

## Use

1. Open the NWD / NWF.
2. Choose a **Scope**:
   * **Scan whole model**, or
   * **Limit to chosen hierarchies**. Select one or more pipe parents (site, zone, pipe…) in the Selection Tree, then press **Add selected** or **Set selected**. You can also press **Import from Hierarchy Scan**. The list is saved for each model.
3. Press **Scan**. The grid then shows one row per line, with these columns: Line No, Area, Size, Spec, BW, SW, Flange, Total, Inch-dia, Pipe m, Inch-meter, Status, Notes. Rows marked CHECK are highlighted yellow.
4. **Click a row** to select that line in the model. You can then use Navisworks *Focus* or *Zoom* on it. Ctrl or Shift-click selects several lines.
5. Use **Show:** to switch the grid between Lines, Area totals, Hierarchy totals and By size. Clicking an area or hierarchy row selects all of its lines.
6. Press **Export Excel** or **Export CSV**. The default file name is `DiaInch_<model>_yyyyMMdd_HHmm.xlsx`.
7. If the results look wrong (zero joints, everything CHECK), select one branch or component and press **Inspect selection**. It shows the type and attributes Dia-Inch can read. Add any missing attribute names to `rules.json` (see below).

### Excel sheets

| Sheet | Content |
|---|---|
| Summary | Model, scope, date, rule switches and grand totals |
| Lines | One row per line: all grid columns plus Screwed, Shop welds, Field welds, All sizes, Hierarchy and Branches |
| Area Totals | Totals per area (zone by default) |
| Hierarchy Totals | Totals per chosen hierarchy, or per model file for a whole-model scan |
| By Size | Joints, inch-dia, length and inch-meter per NB |
| Joints | Every joint found, with line, branch, kind, NB, shop/field, assumed flag, location and note |
| Rules | The rule settings used for this report |

## Rules: `%APPDATA%\MDDiaInch\rules.json`

The first run writes the default file (a copy is in `docs/rules.default.json`). To change the rules:

1. Press **Open rules** and edit the file. `//` comments and trailing commas are allowed.
2. Save the file.
3. Press **Reload rules**, then **Scan** again.

If the file has an error, the panel shows the line and column number and uses the built-in defaults. Your file is not overwritten.

### How joints are counted (defaults)

* **Which items are read.** Each BRANCH is walked in its member order (TUBE and components). These are recognised as components: ELBO, BEND, TEE, CROS, OLET, REDU, FLAN, LJSE, FBLI, VALV, VTWA, VFWA, INST, PCOM, FILT, TRAP, CAP, CLOS, COUP, UNIO and WELD. GASK, BOLT, ATTA and SHU are ignored.
* **Joint type from connection codes.** A joint's type comes from the component connection codes (ACON / LCON / P3CONN…), matched against `connectionCodes`:
  * BW = BW, BWD…
  * SW = SW, SWF, SKT…
  * SCR = SCR, SCF, NPT…
  * FL = FBD, FL, RF, RTJ…
  * NONE = OPEN, CLOS…
  * Unknown codes are listed in Notes and are not counted.
* **Where joints are counted.**
  * There is one joint at each interface between a tube and a component.
  * There is one joint where two components touch (fitting-to-fitting). The higher kind is used: BW > SW > SCR. A FL end against a NONE end counts as FL.
  * There is one joint at each branch outlet: TEE / CROS P3 (and P4), OLET, and the VTWA / VFWA outlets.
* **OLET:** 1 weld to the header (`extraJoints`) plus 1 at the outlet. It does not split the header tube.
* **FLAN / LJSE:** a weld on the pipe side and a flange joint on the face. The ends are oriented using the connection codes.
* **VALV / INST / PCOM / FILT / TRAP:** `AUTO_FL` means the item's own connection codes are used. When a code is missing, the end is assumed: a flange joint above `smallBoreMaxNbMm`, or SW at or below it. The line is then marked CHECK.
* **WELD elements** in the branch count as one joint each and are never double counted with the tube interface.
* **Branch ends (head and tail).** The branch head or tail counts one joint of its HCONN / TCONN kind. The exception is when HREF / TREF points to a tee or olet outlet, or to another branch: that joint is already counted there. When HREF / TREF is empty or outside the scan, the joint is still counted and Notes says so.
* **Flange joints:** they appear in the *Flange* column. They are left out of *Total* and inch-dia unless `flangeJointsInInchDia` = true.
* **Screwed joints:** they are left out of *Total* and inch-dia unless `countScrewedJoints` = true. They are always listed in the *Screwed* column.
* **Joint NB.** The NB at each joint is the bore at that end (ABOR / LBOR / P3BORE…). A reducer uses each end's own bore. Bores are converted from mm to inch with `nbTableMmToInch`: 15 → ½", 25 → 1", 50 → 2", 80 → 3", 100 → 4", 150 → 6", 200 → 8", …, 2000 → 80".
  * Values written as `4"`, `4in`, `1 1/2"` or `DN100` are understood.
  * A value within 2 mm of the table is snapped to it, and the line is marked CHECK.
  * A value outside the table is converted as mm/25.4 and marked CHECK.
* **Tube NB and length.**
  * Tube NB comes from the tube bore. If there is none, it comes from the neighbouring component.
  * If there is still none, it comes from the outside diameter of the geometry (ASME B36.10 OD table, within 4%). Notes says "tube NB from outside diameter". Turn this off with `nbFromGeometry`.
  * Tube length comes from LTLE / Tube Length. If there is none, it comes from the geometry bounding box, and Notes says "length from geometry".
* **Random-length welds.** If `randomPipeLengthM` > 0 (for example 6), a straight tube longer than this adds `ceil(L/6) - 1` butt welds of the tube NB. The default is 0, which means off.
* **Shop / field welds.** Shop and field are split for a line only when every counted weld of that line carries a SHOP or FIELD WELD attribute. Otherwise only totals are reported and that line's Shop and Field cells stay empty.
* **Line number.**
  * If the parent is a PIPE, the line number is the PIPE name with the leading "/" removed.
  * For RVM-style "BRANCH 1 of PIPE /X" names, it is the pipe name.
  * Otherwise it is the branch name with "/B1" removed.
  * A `Line No` attribute wins over all of these. If `lineRegex` is set, its first group (or the whole match) is used.
  * Branches with the same Line No are added together.
* **Area.** By default (`areaMode` = `"zone"`) the area is the ZONE above the pipe. Other modes are `site`, `scope` (the chosen hierarchy) and `parent`. An `:AREA` / `Area` attribute wins.
* **Spec.** The spec is PSPE on the pipe or branch. If there is none, it is the first part of the component SPRE.

### Switches

| Key | Default | Meaning |
|---|---|---|
| `smallBoreMaxNbMm` | 40 | When a connection code is missing, a default `WELD` end is taken as SW at or below this NB (BW above it), and a valve `AUTO_FL` end is taken as SW (FL above it). Assumed ends mark the line CHECK |
| `countScrewedJoints` | false | Add screwed joints to Total and inch-dia |
| `flangeJointsInInchDia` | false | Add flange joints to Total and inch-dia |
| `randomPipeLengthM` | 0 | Extra butt welds on long straight tubes (0 = off) |
| `bareBoreUnit` | "mm" | Unit of numbers without a unit (`mm` or `in`) |
| `lineRegex` | "" | Regex to cut the line number out of names |
| `areaMode` | "zone" | `zone`, `site`, `scope` or `parent` |
| `fieldWhenShopFalse` | true | SHOP = false is read as a field weld |
| `checkWhenConnAssumed` | true | A connection type taken from the defaults (not from the model) marks the line CHECK |
| `nbFromGeometry` | true | Tube NB from the geometry outside diameter when no bore attribute exists |

`propertyAliases` lists the attribute names tried for each value, first match wins. Examples:

* TYPE / "Element Type"
* ACON / "Arrive Connection" / P1CONN
* ABOR / P1BORE
* HREF, LTLE, PSPE, SHOP, "Line No"

Names can be written as `Category > Property` or as a bare property name. Add the names your export uses. **Inspect selection** lists them.

## Known limits

* **Attribute quality.** The result is only as good as the PDMS / E3D attributes in the NWD.
  * An RVM export without attributes gives joint positions from item names and geometry only. Connection types are then assumed and every such line is marked CHECK.
  * Use an export with attributes (ACON / LCON / ABOR / LBOR / HREF / TREF / LTLE) for real figures.
* **Geometry fallbacks.** Bounding boxes include insulation or other geometry under the item. Lengths and ODs from geometry are therefore approximate, and they are always flagged in Notes.
* **Branch references.** HREF / TREF are matched by name. If a referenced branch or component lies outside the chosen hierarchies, the end joint is counted on this side and noted.
* **Joints in two scopes.** A joint shared by two lines is counted once, on the branch that owns the component or outlet.
* **Supports and other items.** Support attachments, bolting and gaskets are not counted.
* **Other kinds of joints.** Field-fit welds, pipe cuts and repairs are not in the model, so they are not counted.
* **Speed.** Large models are read on the Navisworks UI thread. Use **Cancel** to stop; you then get a partial result marked "CANCELLED - partial".
* **Testing.** v1.0.0 was built against the Navisworks 2017–2027 API references and unit-tested (75 tests). It has **not yet been tested live inside Navisworks**. Please check the first project against a manual count.

## Files

* Logs: `%APPDATA%\MDDiaInch\mddiainch.log`
* Rules: `%APPDATA%\MDDiaInch\rules.json`
* Saved scopes: `%APPDATA%\MDDiaInch\areas.txt`

The add-in only reads the model. It writes nothing to the NWD / NWF.

## Build (developer)

`./build_all.sh` runs the unit tests and then builds `build/<year>/MDDiaInch.dll` for 2017 to 2027. `installer/make_release.py` stages the bundle, compiles the setup and builds the portable zip.
