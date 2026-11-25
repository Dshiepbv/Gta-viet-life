@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo === Cai dat cau noi AI tieng Viet (lam 1 lan) ===
py -V:3.11 -m venv .venv || py -3.11 -m venv .venv
if not exist .venv\Scripts\activate.bat (
  echo LOI: chua co Python 3.11. Mo CMD go: py install 3.11  roi chay lai file nay.
  pause
  exit /b 1
)
call .venv\Scripts\activate.bat
python -m pip install --upgrade pip
pip install -r bridge\requirements.txt
if not exist bridge\config.json copy bridge\config.example.json bridge\config.json
echo.
echo Xong. Mo bridge\config.json, dan Gemini API key vao "gemini_api_key".
pause
