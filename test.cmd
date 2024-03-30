@powershell -NoProfile -ExecutionPolicy Bypass -File make.ps1 %* all
@powershell -NoExit -NoProfile -ExecutionPolicy Bypass -File make.ps1 %* test
