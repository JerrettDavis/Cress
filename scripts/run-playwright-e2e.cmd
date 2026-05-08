@echo off
setlocal
"C:\Program Files\nodejs\node.exe" "%~dp0..\node_modules\@playwright\test\cli.js" test --config "%~dp0..\playwright.config.ts"
