@echo off
chcp 65001 > nul
title AwadTimetable - Git Updater

echo ==========================================
echo      NOW ANLYZIZ...
echo ==========================================
git status

echo.
set /p commit_msg="Type a discpition or press ENTER ): "
if "%commit_msg%"=="" set commit_msg=Update AwadTimetable %date% %time%

echo.
echo [1/3] UPDATEEING (git add)...
git add .

echo [2/3] SAVVING (git commit)...
git commit -m "%commit_msg%"

echo [3/3] UPLAODING  GitHub (git push)...
git push

echo.
echo ==========================================
echo         DONE!
echo ==========================================
pause