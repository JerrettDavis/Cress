@echo off
setlocal
set CRESS_CAPTURE_DOCS_SCREENSHOTS=1
"C:\Program Files\nodejs\node.exe" "%~dp0..\node_modules\@playwright\test\cli.js" test --config "%~dp0..\playwright.config.ts"
