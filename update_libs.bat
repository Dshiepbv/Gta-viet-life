@echo off
chcp 65001 >nul
cd /d "%~dp0"
call .venv\Scripts\activate.bat
pip install -U edge-tts gTTS
echo.
echo Xong. Chay lai start_bridge.bat
pause
