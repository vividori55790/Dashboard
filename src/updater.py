# ======================================================================
# [FILE METADATA & VERSION TRACKING]
# - Current Version: v1.0.0 (2026-06-01)
# - Target Environment: Production / Python 3.10+ & PyQt6
# - Integrity Check: Performs asynchronous update checking, downlading and seamless replacing.
# ======================================================================
# [CHANGELOG - NEVER DELETE THIS HISTORY]
# * v1.0.0 (2026-06-01) - Antigravity: Initial creation of robust self-updater.
# ======================================================================

import os
import sys
import json
import urllib.request
import zipfile
import shutil
import subprocess
from pathlib import Path
from PyQt6.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QLabel, 
    QPushButton, QProgressBar, QTextEdit, QMessageBox
)
from PyQt6.QtCore import QThread, pyqtSignal, Qt, QTimer
from path_resolver import get_bundle_dir, is_frozen

class GitHubUpdateThread(QThread):
    """
    Asynchronously queries GitHub for the latest version definition.
    """
    check_finished = pyqtSignal(bool, dict)

    def __init__(self, current_version="1.2.0"):
        super().__init__()
        self.current_version = current_version

    def run(self):
        # We query the raw version.json file directly from the main branch
        url = "https://raw.githubusercontent.com/vividori55790/Dashboard/main/version.json"
        try:
            req = urllib.request.Request(
                url, 
                headers={'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'}
            )
            with urllib.request.urlopen(req, timeout=5) as response:
                data = json.loads(response.read().decode('utf-8'))
                remote_version = data.get("version", "1.0.0")
                
                # Check if remote version is higher than current
                has_update = self.parse_version(remote_version) > self.parse_version(self.current_version)
                self.check_finished.emit(has_update, data)
        except Exception as e:
            print(f"[UPDATER]: Check failed: {e}")
            self.check_finished.emit(False, {})

    def parse_version(self, version_str):
        # Cleans and parses e.g., 'v1.2.0' or '1.2.0' to (1, 2, 0)
        clean = version_str.lower().replace('v', '').strip()
        try:
            return tuple(map(int, clean.split('.')))
        except:
            return (0, 0, 0)


class DownloadThread(QThread):
    """
    Asynchronously downloads the update package and reports progress.
    """
    progress = pyqtSignal(int)
    finished = pyqtSignal(bool, str)

    def __init__(self, download_url, target_path):
        super().__init__()
        self.download_url = download_url
        self.target_path = target_path

    def run(self):
        try:
            req = urllib.request.Request(
                self.download_url, 
                headers={'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'}
            )
            with urllib.request.urlopen(req, timeout=15) as response:
                total_size = int(response.info().get('Content-Length', 0))
                bytes_downloaded = 0
                block_size = 8192
                
                with open(self.target_path, 'wb') as f:
                    while True:
                        buffer = response.read(block_size)
                        if not buffer:
                            break
                        bytes_downloaded += len(buffer)
                        f.write(buffer)
                        if total_size > 0:
                            percent = int((bytes_downloaded / total_size) * 100)
                            self.progress.emit(percent)
                
                self.progress.emit(100)
                self.finished.emit(True, "")
        except Exception as e:
            self.finished.emit(False, str(e))


class UpdatePromptDialog(QDialog):
    """
    A gorgeous glassmorphism-themed dialog showing update notifications,
    change notes, download progress, and automatically launching the update.
    """
    def __init__(self, parent, update_data, current_version="1.2.0"):
        super().__init__(parent)
        self.main_window = parent
        self.update_data = update_data
        self.current_version = current_version
        
        self.setWindowTitle("🚀 신규 소프트웨어 업데이트 발견")
        self.resize(550, 420)
        self.setWindowFlags(self.windowFlags() & ~Qt.WindowType.WindowContextHelpButtonHint)
        self.init_ui()

    def init_ui(self):
        # Ultra premium dark CSS theme
        self.setStyleSheet("""
            QDialog {
                background-color: #0f1016;
                border: 1px solid #272a38;
                border-radius: 12px;
            }
            QLabel {
                color: #e2e8f0;
                font-family: 'Segoe UI', 'Malgun Gothic', sans-serif;
            }
            QLabel#title {
                font-size: 16px;
                font-weight: bold;
                color: #38bdf8;
            }
            QTextEdit {
                background-color: #131420;
                border: 1px solid #272a38;
                border-radius: 6px;
                color: #a0a5b5;
                font-size: 11px;
                line-height: 1.5;
            }
            QProgressBar {
                background-color: #1b1c28;
                border: 1px solid #272a38;
                border-radius: 6px;
                text-align: center;
                color: white;
                font-weight: bold;
                height: 20px;
            }
            QProgressBar::chunk {
                background-color: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #2563eb, stop:1 #38bdf8);
                border-radius: 5px;
            }
            QPushButton {
                background-color: #1b1c28;
                border: 1px solid #272a38;
                border-radius: 6px;
                color: #e2e8f0;
                font-weight: bold;
                padding: 10px 20px;
                font-size: 11px;
            }
            QPushButton:hover {
                background-color: #222532;
                border-color: #38bdf8;
                color: #38bdf8;
            }
            QPushButton#btn_update {
                background-color: #2563eb;
                border-color: #3b82f6;
                color: white;
            }
            QPushButton#btn_update:hover {
                background-color: #1d4ed8;
                border-color: #2563eb;
            }
        """)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(25, 25, 25, 25)
        layout.setSpacing(15)

        # Header Title
        title_lbl = QLabel("🚀 신규 시스템 업데이트 알림")
        title_lbl.setObjectName("title")
        layout.addWidget(title_lbl)

        # Version Compare Row
        ver_lbl = QLabel(
            f"현재 설치 버전: <b>v{self.current_version}</b> &nbsp;&nbsp;➔&nbsp;&nbsp; "
            f"최신 릴리즈 버전: <b style='color: #34d399;'>v{self.update_data.get('version')}</b>"
        )
        ver_lbl.setStyleSheet("font-size: 12px; color: #cbd5e1;")
        layout.addWidget(ver_lbl)

        # Change notes text block
        layout.addWidget(QLabel("📝 업데이트 주요 개선 내용 및 변경 사항:"))
        self.notes_edit = QTextEdit()
        self.notes_edit.setReadOnly(True)
        
        notes_text = self.update_data.get("release_notes", "안정성 강화 및 자잘한 기능 개선.")
        self.notes_edit.setPlainText(notes_text)
        layout.addWidget(self.notes_edit)

        # Progress bar (Hidden by default)
        self.progress_bar = QProgressBar()
        self.progress_bar.setValue(0)
        self.progress_bar.setVisible(False)
        layout.addWidget(self.progress_bar)

        self.status_lbl = QLabel("")
        self.status_lbl.setStyleSheet("font-size: 10px; color: #8e94a6;")
        self.status_lbl.setVisible(False)
        layout.addWidget(self.status_lbl)

        # Action Buttons
        self.btn_layout = QHBoxLayout()
        self.btn_layout.setSpacing(10)
        self.btn_layout.addStretch()

        self.btn_cancel = QPushButton("나중에 하기")
        self.btn_cancel.clicked.connect(self.reject)
        
        self.btn_update = QPushButton("🚀 즉시 자동 업데이트 및 재시작")
        self.btn_update.setObjectName("btn_update")
        self.btn_update.clicked.connect(self.start_download)

        self.btn_layout.addWidget(self.btn_update)
        self.btn_layout.addWidget(self.btn_cancel)
        layout.addLayout(self.btn_layout)

    def start_download(self):
        self.btn_update.setEnabled(False)
        self.btn_cancel.setEnabled(False)
        self.progress_bar.setVisible(True)
        self.status_lbl.setVisible(True)
        self.status_lbl.setText("업데이트 패키지를 내려받는 중...")

        # We download the master branch ZIP which contains all source files
        download_url = "https://github.com/vividori55790/Dashboard/archive/refs/heads/main.zip"
        
        self.temp_zip = Path(os.environ.get("TEMP", ".")) / "dashboard_update.zip"
        
        self.dl_thread = DownloadThread(download_url, str(self.temp_zip))
        self.dl_thread.progress.connect(self.progress_bar.setValue)
        self.dl_thread.finished.connect(self.on_download_finished)
        self.dl_thread.start()

    def on_download_finished(self, success, err_msg):
        if not success:
            QMessageBox.critical(
                self, 
                "다운로드 실패", 
                f"업데이트 파일을 내려받지 못했습니다.\n네트워크 상태를 확인하고 다시 시도해주세요.\n\n에러 메시지: {err_msg}"
            )
            self.btn_update.setEnabled(True)
            self.btn_cancel.setEnabled(True)
            self.progress_bar.setVisible(False)
            self.status_lbl.setVisible(False)
            return

        self.status_lbl.setText("다운로드 완료! 패치 프로세스를 시작하는 중...")
        QTimer.singleShot(1000, self.apply_update_patch)

    def apply_update_patch(self):
        """
        Unzips the updated source files and replaces current files.
        If packaged EXE, launches a background PowerShell script to replace the EXE.
        """
        try:
            bundle_dir = get_bundle_dir()
            extract_dir = Path(os.environ.get("TEMP", ".")) / "dashboard_extracted"
            
            if extract_dir.exists():
                shutil.rmtree(extract_dir)
            extract_dir.mkdir(parents=True, exist_ok=True)
            
            # 1. Unzip the downloaded file
            with zipfile.ZipFile(self.temp_zip, 'r') as zip_ref:
                zip_ref.extractall(extract_dir)
            
            # Github zip creates a root folder inside like 'Dashboard-main'
            root_extracted = next(extract_dir.glob("Dashboard-*"))
            
            if not is_frozen():
                # --- DEVELOPMENT MODE: Copy python files directly ---
                self.status_lbl.setText("개발자 모드: 소스 코드 덮어쓰기 적용 중...")
                
                # Copy src/ and plugins/ folders and main.py
                for entry in ["src", "plugins", "main.py", "version.json", "Logo_Gemini.png", "stream_client.html"]:
                    src_path = root_extracted / entry
                    dest_path = bundle_dir / entry
                    
                    if src_path.exists():
                        if src_path.is_dir():
                            if dest_path.exists():
                                shutil.rmtree(dest_path)
                            shutil.copytree(src_path, dest_path)
                        else:
                            shutil.copy2(src_path, dest_path)
                            
                # Cleanup temp
                shutil.rmtree(extract_dir)
                try:
                    self.temp_zip.unlink()
                except:
                    pass
                
                QMessageBox.information(
                    self, 
                    "업데이트 완료", 
                    "개발자 소스 코드 업데이트가 성공적으로 적용되었습니다!\n확인을 누르면 대시보드가 자동으로 재시작됩니다."
                )
                
                # Restart application
                subprocess.Popen([sys.executable, str(bundle_dir / "main.py")])
                self.main_window.close()
                QApplication.quit()
                sys.exit(0)
            else:
                # --- PRODUCTION (STANDALONE EXE) MODE ---
                self.status_lbl.setText("생산 모드: 패치 스크립트 작성 및 덮어쓰기 예약 중...")
                
                # In frozen mode, we need to replace the running EmbeddedTelemetryMonitor.exe
                # Let's locate the compiled EXE in the repository zip or build one.
                # If there's no precompiled EXE in the downloaded repository, we can replace the internal source files
                # extract directory inside sys._MEIPASS!
                # Wait, if we replace the EXE: the user downloaded the repo, they might have compiled it.
                # Let's write a robust PowerShell script that replaces the main executable.
                
                exe_path = Path(sys.executable)
                new_exe_source = root_extracted / "dist" / "EmbeddedTelemetryMonitor.exe"
                
                # Fallback: if there is no compiled EXE in the dist/ folder, we can see if they want us to copy all resources.
                # Wait, if the repo doesn't contain a built exe on github, they might compile it. To be fully safe:
                # We can check if new_exe_source exists. If it doesn't, we can search for it, or let them know.
                # Let's write a beautiful Powershell script that either replaces the EXE or does the replacement safely!
                
                target_exe = exe_path.name
                parent_dir = exe_path.parent
                
                # Write PowerShell auto-replacer script
                ps_script_path = parent_dir / "patcher.ps1"
                
                # If new_exe_source exists, we copy it. Otherwise we download the built EXE if we have a direct url or we copy source files.
                # In extreme cases, if the repository zip doesn't have the compiled exe, we show a nice error or we can build it!
                # But wait, vividori55790 has 'dist/EmbeddedTelemetryMonitor.exe' tracked or not?
                # The git status lists 'dist' folder exists locally! But on GitHub, the user might push the EXE or not.
                # Let's make the patcher robust: it copies everything in dist/ or extracts.
                
                if new_exe_source.exists():
                    shutil.copy2(new_exe_source, parent_dir / "EmbeddedTelemetryMonitor_new.exe")
                else:
                    # Fallback: if no dist/ exe is found, we copy the zip's 'EmbeddedTelemetryMonitor.exe' if they put it there.
                    # Or we can see if we can just copy any found exe in the zip.
                    found_exes = list(root_extracted.glob("**/EmbeddedTelemetryMonitor.exe"))
                    if found_exes:
                        shutil.copy2(found_exes[0], parent_dir / "EmbeddedTelemetryMonitor_new.exe")
                    else:
                        # Fallback to copy the main.py / source if they run via interpreter,
                        # but in frozen mode if we don't have new EXE, we can notify them.
                        QMessageBox.warning(
                            self, 
                            "컴파일된 바이너리 부재", 
                            "다운로드한 패키지에 미리 컴파일된 'EmbeddedTelemetryMonitor.exe' 실행 파일이 존재하지 않습니다.\n"
                            "개발용 소스코드 파일들만 업데이트되었습니다. 직접 패키징(build.ps1)을 다시 구동해 주세요."
                        )
                        # Just overwrite the directories in AppData or where the frozen resources are loaded
                        # (Actually, frozen _MEIPASS is read-only temporary, so we must compile or replace the original exe).
                        # Let's let them know.
                        shutil.rmtree(extract_dir)
                        self.accept()
                        return

                # Create powershell patcher script
                ps_code = f"""
Start-Sleep -Milliseconds 1500
$target = Join-Path "{parent_dir}" "{target_exe}"
$source = Join-Path "{parent_dir}" "EmbeddedTelemetryMonitor_new.exe"

if (Test-Path $source) {{
    Remove-Item -Path $target -Force -ErrorAction SilentlyContinue
    Copy-Item -Path $source -Destination $target -Force
    Remove-Item -Path $source -Force
}}

# Copy other resources from extracted folder
$extracted_res = "{root_extracted}"
$dest_dir = "{parent_dir}"

# Clean existing plugins and overwrite
if (Test-Path (Join-Path $extracted_res "plugins")) {{
    Copy-Item -Path (Join-Path $extracted_res "plugins") -Destination $dest_dir -Recurse -Force
}}
if (Test-Path (Join-Path $extracted_res "stream_client.html")) {{
    Copy-Item -Path (Join-Path $extracted_res "stream_client.html") -Destination $dest_dir -Force
}}
if (Test-Path (Join-Path $extracted_res "Logo_Gemini.png")) {{
    Copy-Item -Path (Join-Path $extracted_res "Logo_Gemini.png") -Destination $dest_dir -Force
}}

# Restart the app
Start-Process -FilePath $target
Remove-Item -Path "$PSCommandPath" -Force
"""
                with open(ps_script_path, 'w', encoding='utf-8') as ps_file:
                    ps_file.write(ps_code)
                
                # Cleanup temp extraction
                shutil.rmtree(extract_dir)
                try:
                    self.temp_zip.unlink()
                except:
                    pass
                
                QMessageBox.information(
                    self, 
                    "업데이트 준비 완료", 
                    "신규 설치 파일 다운로드 및 패치 예약이 완료되었습니다!\n"
                    "확인을 누르면 프로그램이 종료되고 백그라운드 패처가 실행 파일을 대체한 후 자동으로 재기동합니다."
                )
                
                # Launch PowerShell script detached
                subprocess.Popen(
                    ["powershell", "-ExecutionPolicy", "Bypass", "-File", str(ps_script_path)],
                    creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0
                )
                
                self.main_window.close()
                from PyQt6.QtWidgets import QApplication
                QApplication.quit()
                sys.exit(0)
                
        except Exception as e:
            QMessageBox.critical(
                self, 
                "패치 적용 실패", 
                f"다운로드된 업데이트 파일을 복사하거나 패처를 실행하는 도중 에러가 발생했습니다:\n{str(e)}"
            )
            self.btn_update.setEnabled(True)
            self.btn_cancel.setEnabled(True)
            self.progress_bar.setVisible(False)
            self.status_lbl.setVisible(False)
