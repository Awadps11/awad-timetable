@echo off
chcp 65001 > nul
title AwadTimetable - Git Updater

echo ==========================================
echo      جاري فحص حالة الملفات في المشروع...
echo ==========================================
git status

echo.
set /p commit_msg="اكتب وصف التحديث (أو اضغط Enter للوصف التلقائي): "
if "%commit_msg%"=="" set commit_msg=Update AwadTimetable %date% %time%

echo.
echo [1/3] جاري إضافة الملفات (git add)...
git add .

echo [2/3] جاري حفظ التغييرات (git commit)...
git commit -m "%commit_msg%"

echo [3/3] جاري الرفع إلى GitHub (git push)...
git push

echo.
echo ==========================================
echo          تم تحديث الريبو بنجاح!
echo ==========================================
pause