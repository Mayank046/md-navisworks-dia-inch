#!/usr/bin/env python3
"""MD Dia-Inch release builder: stage bundle, generate .iss from the no-admin template, compile, portable zip, SHA256SUMS.
Author: MD (Mayank Dholariya)"""
import hashlib, os, re, shutil, subprocess, sys, zipfile
from xml.sax.saxutils import quoteattr as qa

VER = "1.0.0"
NAME = "MDDiaInch"; APP = "MD Dia-Inch"; BUNDLE = "MD.DiaInch.bundle"
AUTHOR = "MD (Mayank Dholariya)"
DESC = "Weld joints, inch-dia and inch-meter per pipeline for AVEVA PDMS/E3D models"
APPID = "A4775A31-0063-4434-A4BD-E9C1C2C0C899"           # Inno AppId (keep for upgrades)
PRODUCT = "{8351C160-93CF-436B-9752-29F92A272B97}"       # PackageContents ProductCode
UPGRADE = "{800D1BDD-89D5-4BDD-866C-25A8FEAD6D59}"       # PackageContents UpgradeCode
YEARS = range(2017, 2028)
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def find_iscc():
    cands = []
    if os.environ.get("ISCC"):
        cands.append(os.environ["ISCC"])
    local = os.environ.get("LOCALAPPDATA") or ""
    if local:
        cands.append(os.path.join(local, "Programs", "Inno Setup 6", "ISCC.exe"))
    cands += [
        r"C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        r"C:\Program Files\Inno Setup 6\ISCC.exe",
        os.path.expanduser("~/.wine-inno/drive_c/Program Files (x86)/Inno Setup 6/ISCC.exe"),
        os.path.expanduser("~/.wine-inno/drive_c/Program Files/Inno Setup 6/ISCC.exe"),
    ]
    for c in cands:
        if c and os.path.isfile(c):
            return c
    sys.exit("ISCC.exe not found. Install Inno Setup 6, or set the ISCC environment variable to ISCC.exe.")

def rel_iss(p):
    """Path relative to installer/, using backslashes, for the committed .iss file."""
    return os.path.relpath(p, os.path.join(ROOT, "installer")).replace("/", "\\")

def wine_path(p):
    if os.name == "nt":
        return p
    p = os.path.abspath(p)
    return "Z:" + p.replace("/", "\\")

TEMPLATE = os.path.join(ROOT, "installer", "Plugin_NoAdmin.iss.template")
REL = os.path.join(ROOT, "release"); STAGE = os.path.join(ROOT, "stage"); B = os.path.join(STAGE, BUNDLE)
SETUP = f"MD_Navisworks_DiaInch_Setup_v{VER}"; ZIPN = f"MD_Navisworks_DiaInch_Portable_v{VER}.zip"
def z(p): return "Z:" + p.replace("/", "\\")

def stage():
    shutil.rmtree(STAGE, ignore_errors=True); os.makedirs(os.path.join(B, "Contents"))
    shutil.copy(os.path.join(ROOT, "icon", NAME + ".ico"), os.path.join(B, "Contents"))
    comps = []
    for y in YEARS:
        d = os.path.join(B, "Contents", str(y)); os.makedirs(os.path.join(d, "Images"))
        dll = os.path.join(ROOT, "build", str(y), NAME + ".dll")
        if not os.path.isfile(dll): sys.exit("missing " + dll)
        shutil.copy(dll, d)
        for s in (16, 32): shutil.copy(os.path.join(ROOT, "icon", f"{NAME}_{s}.png"), os.path.join(d, "Images"))
        comps.append(f'  <Components Description="Navisworks {y}">\n'
                     f'    <RuntimeRequirements OS="Win64" Platform="NAVMAN|NAVSIM" SeriesMin="Nw{y-2003}" SeriesMax="Nw{y-2003}" />\n'
                     f'    <ComponentEntry AppName={qa(APP)} AppType="ManagedPlugin" ModuleName="./Contents/{y}/{NAME}.dll" AppDescription={qa(DESC)} />\n'
                     f'  </Components>')
    xml = ('<?xml version="1.0" encoding="utf-8"?>\n'
           f'<ApplicationPackage SchemaVersion="1.0" AutodeskProduct="Navisworks" ProductType="Application" Name={qa(APP)} '
           f'AppVersion="{VER}" Description={qa(DESC)} Author={qa(AUTHOR)} ProductCode="{PRODUCT}" UpgradeCode="{UPGRADE}" '
           f'Icon="./Contents/{NAME}.ico">\n  <CompanyDetails Name={qa(AUTHOR)} />\n' + "\n".join(comps) + "\n</ApplicationPackage>\n")
    open(os.path.join(B, "PackageContents.xml"), "w", encoding="utf-8").write(xml)

def iss():
    s = open(TEMPLATE, encoding="utf-8-sig").read()
    tok = {"__APP__": APP, "__VER__": VER, "__APPID__": APPID, "__REL__": rel_iss(REL), "__SETUP__": SETUP,
           "__ICON__": rel_iss(os.path.join(ROOT, "icon", NAME + ".ico")),
           "__WIZL__": rel_iss(os.path.join(ROOT, "icon", "wizard_large.bmp")),
           "__WIZS__": rel_iss(os.path.join(ROOT, "icon", "wizard_small.bmp")),
           "__NAME__": NAME, "__BUNDLE__": BUNDLE, "__STAGE__": rel_iss(B)}
    for k, v in tok.items():
        if k not in s: sys.exit("token not in template: " + k)
        s = s.replace(k, v)
    left = re.findall(r"__[A-Z]+__", s)
    if left: sys.exit("unfilled tokens: " + ", ".join(sorted(set(left))))
    p = os.path.join(ROOT, "installer", "DiaInch_NoAdmin.iss")
    open(p, "w", encoding="utf-8-sig", newline=chr(13)+chr(10)).write(s)
    return p

def compile_(p):
    iscc = find_iscc()
    if os.name == "nt":
        r = subprocess.run([iscc, "/Q", p], capture_output=True, text=True)
    else:
        env = dict(os.environ, WINEPREFIX=os.environ.get("WINEPREFIX", os.path.expanduser("~/.wine-inno")), WINEDEBUG="-all")
        r = subprocess.run(["wine", wine_path(iscc), "/Q", wine_path(p)], env=env, capture_output=True, text=True)
    if r.returncode != 0:
        print((r.stdout or "")[-3000:], (r.stderr or "")[-3000:])
        sys.exit("ISCC failed")


def portable():
    out = os.path.join(REL, ZIPN); top = f"MD_DiaInch_Portable_v{VER}/"
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
        for dp, _, fs in os.walk(B):
            for f in sorted(fs):
                full = os.path.join(dp, f); zf.write(full, top + "bundles/" + os.path.relpath(full, STAGE).replace(os.sep, "/"))
        for f in ("install.cmd", "uninstall.cmd", "check.cmd", "md_diainch.ps1"):
            zf.write(os.path.join(ROOT, "installer", "portable", f), top + f)
        zf.write(os.path.join(ROOT, "README.md"), top + "README.md")
        zf.write(os.path.join(ROOT, "docs", "rules.default.json"), top + "rules.default.json")

def main():
    os.makedirs(REL, exist_ok=True)
    for f in os.listdir(REL): os.remove(os.path.join(REL, f))   # only this release's folder
    stage(); compile_(iss()); portable()
    shutil.copy(os.path.join(ROOT, "README.md"), os.path.join(REL, f"README_MD_DiaInch_v{VER}.md"))
    shutil.copy(os.path.join(ROOT, "docs", "rules.default.json"), os.path.join(REL, "rules.default.json"))
    shutil.copy(os.path.join(ROOT, "docs", "DiaInch_sample_demo.xlsx"), os.path.join(REL, "DiaInch_sample_demo.xlsx"))
    lines = []
    for f in sorted(os.listdir(REL)):
        if f == "SHA256SUMS": continue
        lines.append(hashlib.sha256(open(os.path.join(REL, f), "rb").read()).hexdigest() + "  " + f)
    open(os.path.join(REL, "SHA256SUMS"), "w").write("\n".join(lines) + "\n")
    print("\n".join(lines))

if __name__ == "__main__": main()
