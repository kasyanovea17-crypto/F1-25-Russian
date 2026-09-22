#!/usr/bin/env python3
"""Executable Python rollback wrapper. Usage: python ROLLBACK.sh GAME [--test]."""
from pathlib import Path
import subprocess,sys
if len(sys.argv) not in (2,3) or (len(sys.argv)==3 and sys.argv[2]!='--test'):
 print('Usage: python ROLLBACK.sh "C:\\path\\to\\F1 25" [--test]',file=sys.stderr)
 sys.exit(2)
cmd=['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(Path(__file__).resolve().parent/'Engine.ps1'),'-Action','restore','-GamePath',sys.argv[1]]
if len(sys.argv)==3:cmd.append('-TestMode')
sys.exit(subprocess.call(cmd))
