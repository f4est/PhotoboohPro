import tkinter as tk
from tkinter import messagebox, filedialog, simpledialog, ttk
from threading import Thread
import datetime
import os
import qrcode
import cv2
import numpy as np
from PIL import Image, ImageTk
from google.oauth2 import service_account
from googleapiclient.discovery import build
from googleapiclient.http import MediaFileUpload
import queue
import threading
import time
import sounddevice as sd
import soundfile as sf
import subprocess
import vlc

# Google Drive config
SERVICE_ACCOUNT_FILE = 'photoboothproject-459010-c725b2899f7f.json'
SCOPES = ['https://www.googleapis.com/auth/drive']
EVENTS_FOLDER_ID = '1oHDqcrZnRcnNCDwGsifDSGVQZjYqtf1S'
UNIVERSAL_FOLDER_ID = '1FR92J38OPdLZoCaKKZ6lW7EucdGtG624'

try:
    credentials = service_account.Credentials.from_service_account_file(
        SERVICE_ACCOUNT_FILE, scopes=SCOPES)
    drive_service = build('drive', 'v3', credentials=credentials)
except Exception as e:
    drive_service = None
    print(f"Failed to initialize Google Drive service: {e}")

SAVE_DIR = os.path.abspath("recordings")
os.makedirs(SAVE_DIR, exist_ok=True)

DEVICE_NAME = "EOS Webcam Utility"
camera_index = 0
overlay_image_path = None
overlay_image_cv = None
cap = None
preview_running = False
video_loop_active = False
video_loop_cap = None
recording = False
out = None
recording_filename = None
last_uni_folder_id = None
countdown_value = None
audio_path = None
audio_thread = None
countdown_active = False  # Для управления отсчётом
ROTATION_OPTIONS = {
    "Без поворота": None,
    "90° вправо (вертикально)": cv2.ROTATE_90_CLOCKWISE,
    "90° влево (вертикально)": cv2.ROTATE_90_COUNTERCLOCKWISE,
    "180°": cv2.ROTATE_180
}

def list_events():
    if drive_service is None:
        print("Google Drive service not initialized.")
        return [], {}
    try:
        res = drive_service.files().list(
            q=f"'{EVENTS_FOLDER_ID}' in parents and mimeType='application/vnd.google-apps.folder' and trashed=false",
            spaces='drive', fields='files(id,name)', pageSize=1000
        ).execute()
        files = res.get('files', [])
        return [f['name'] for f in files], {f['name']: f['id'] for f in files}
    except Exception as e:
        print(f"Failed to load events list: {e}")
        return [], {}

def create_event(name):
    if drive_service is None:
        print("Google Drive service not initialized.")
        return None
    try:
        meta = {'name': name, 'mimeType': 'application/vnd.google-apps.folder', 'parents': [EVENTS_FOLDER_ID]}
        folder = drive_service.files().create(body=meta, fields='id').execute()
        return folder['id']
    except Exception as e:
        print(f"Failed to create event: {e}")
        return None

def upload_to_drive(path, folder_name, event_folder_id, reuse_last=False, last_folder_id=None):
    if drive_service is None:
        print("Google Drive service not initialized.")
        return None, None, None
    if not os.path.exists(path):
        print(f"File {path} not found.")
        return None, None, None
    
    max_retries = 3
    for attempt in range(max_retries):
        try:
            media = MediaFileUpload(path, mimetype='video/mp4')
            drive_service.files().create(
                body={'name': os.path.basename(path), 'parents': [event_folder_id]},
                media_body=media
            ).execute()
            if reuse_last and last_folder_id:
                uni_id = last_folder_id
            else:
                uni_meta = {'name': folder_name, 'mimeType': 'application/vnd.google-apps.folder', 'parents': [UNIVERSAL_FOLDER_ID]}
                uni_folder = drive_service.files().create(body=uni_meta, fields='id').execute()
                uni_id = uni_folder['id']
            drive_service.files().create(
                body={'name': os.path.basename(path), 'parents': [uni_id]},
                media_body=media
            ).execute()
            uni_url = f'https://drive.google.com/drive/folders/{uni_id}'
            qr = qrcode.make(uni_url)
            return qr, uni_url, uni_id
        except Exception as e:
            if attempt < max_retries - 1:
                time.sleep(2 ** attempt)
            else:
                print(f"Failed to upload file to Google Drive after {max_retries} attempts: {e}")
                return None, None, None

def record_audio(path, duration_s, mic_name):
    print(f"Starting audio recording to {path} for {duration_s} seconds with mic: {mic_name}")
    devs = sd.query_devices()
    idx = next((i for i, d in enumerate(devs) if d['name'] == mic_name), None)
    if idx is None:
        print(f"Error: Microphone {mic_name} not found.")
        return
    try:
        samplerate = 44100
        audio = sd.rec(int(duration_s * samplerate),
                       samplerate=samplerate,
                       channels=1,
                       dtype='int16',
                       device=idx)
        sd.wait()
        sf.write(path, audio, samplerate)
        print(f"Audio recording saved to {path}")
    except Exception as e:
        print(f"Error recording audio: {e}")

def load_overlay():
    global overlay_image_path, overlay_image_cv
    file = filedialog.askopenfilename(filetypes=[("Image Files", ".png;.jpg;*.jpeg")])
    if not file:
        return
    img = cv2.imread(file, cv2.IMREAD_UNCHANGED)
    if img is None:
        print("Failed to load overlay image.")
        return
    overlay_image_path = file
    h, w = img.shape[:2]
    scale = 1200 / w
    nh = int(h * scale)
    resized = cv2.resize(img, (1200, nh))
    if nh > 1800:
        off = (nh - 1800) // 2
        overlay_image_cv = resized[off:off+1800, :]
    else:
        pad = (1800 - nh) // 2
        overlay_image_cv = cv2.copyMakeBorder(resized, pad, 1800-nh-pad, 0, 0, cv2.BORDER_CONSTANT)
    print("Overlay loaded successfully.")

def clear_overlay():
    global overlay_image_path, overlay_image_cv
    overlay_image_path = None
    overlay_image_cv = None
    print("Overlay cleared.")

preview_queue = queue.Queue(maxsize=10)

def process_frames():
    global cap, overlay_image_cv, countdown_value
    while preview_running:
        if not (cap and cap.isOpened()):
            time.sleep(0.01)
            continue
        ret, frame = cap.read()
        if not ret:
            time.sleep(0.01)
            continue
        rot = ROTATION_OPTIONS[selected_rotation.get()]
        if rot is not None:
            frame = cv2.rotate(frame, rot)
        h, w = frame.shape[:2]
        scale = 600 / w
        nh = int(h * scale)
        frame = cv2.resize(frame, (600, nh))
        if nh > 900:
            off = (nh - 900) // 2
            frame = frame[off:off+900, :]
        else:
            pad = (900 - nh) // 2
            frame = cv2.copyMakeBorder(frame, pad, 900-nh-pad, 0, 0, cv2.BORDER_CONSTANT)
        if overlay_image_cv is not None:
            ov = cv2.resize(overlay_image_cv, (600, 900))
            if ov.shape[2] == 4:
                alpha = ov[:, :, 3:] / 255.0
                rgb = ov[:, :, :3]
                frame = (frame * (1 - alpha) + rgb * alpha).astype(np.uint8)
            else:
                frame = cv2.addWeighted(frame, 0.7, ov, 0.3, 0)
        if countdown_value is not None:
            txt = str(countdown_value)
            org = (frame.shape[1]//2 - 60, frame.shape[0]//2 + 60)
            cv2.putText(frame, txt, org,
                        cv2.FONT_HERSHEY_SIMPLEX, 5,
                        (255,255,255), 10, cv2.LINE_AA)
        try:
            preview_queue.put_nowait(frame)
        except queue.Full:
            pass
        time.sleep(0.01)

def update_preview():
    global cap, preview_running
    if cap and cap.isOpened():
        cap.release()
    cap = cv2.VideoCapture(camera_index, cv2.CAP_DSHOW)
    if not cap.isOpened():
        print("Failed to open camera for preview.")
        return
    cap.set(cv2.CAP_PROP_FPS, 30)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1200)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 1800)
    preview_running = True
    threading.Thread(target=process_frames, daemon=True).start()
    def loop():
        if not preview_running:
            return
        try:
            frame = preview_queue.get_nowait()
            preview_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            imgtk = ImageTk.PhotoImage(Image.fromarray(preview_rgb))
            preview_label.imgtk = imgtk
            preview_label.config(image=imgtk)
        except queue.Empty:
            pass
        preview_label.after(33, loop)
    loop()

def stop_preview():
    global cap, preview_running
    preview_running = False
    if cap and cap.isOpened():
        cap.release()
        cap = None
    preview_label.config(image='')

def start_countdown(sec, callback):
    global countdown_value, countdown_active
    def finish():
        global countdown_value
        countdown_value = None
        callback()
    def tick(n):
        global countdown_value, countdown_active
        if not countdown_active:
            return
        countdown_value = n
        print(countdown_value)
        if n > 0:
            window.after(1000, lambda: tick(n-1))
        else:
            window.after(1000, finish)
    countdown_active = True
    tick(sec)

def start_recording_countdown(sec, callback):
    global countdown_active
    countdown_label = tk.Label(main_page, text=str(sec), font=("Helvetica", 150), fg="white", bg="#000000")
    countdown_label.place(relx=0.5, rely=0.5, anchor="center")
    def tick(n):
        global countdown_active
        if not countdown_active:
            countdown_label.place_forget()
            return
        countdown_label.config(text=str(n))
        if n > 0:
            window.after(1000, lambda: tick(n-1))
        else:
            window.after(1000, lambda: [countdown_label.place_forget(), callback()])
    countdown_active = True
    tick(sec)

def start_recording():
    global countdown_active
    countdown_active = False  # Сбрасываем предыдущий отсчёт, если он был
    btn_start.config(state=tk.DISABLED)
    start_countdown(3, begin_recording)

def begin_recording():
    global cap, recording, out, recording_filename, preview_running, audio_path, audio_thread
    preview_running = False
    stop_preview()
    time.sleep(0.5)
    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    recording_filename = f"video_{ts}.mp4"
    out_path = os.path.join(SAVE_DIR, recording_filename)

    cap = cv2.VideoCapture(camera_index, cv2.CAP_DSHOW)
    if not cap.isOpened():
        print("Failed to open camera for recording.")
        btn_start.config(state=tk.NORMAL)
        show_main_page()
        return
    cap.set(cv2.CAP_PROP_FPS, 15)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1200)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 1800)

    fourcc = cv2.VideoWriter_fourcc(*'mp4v')
    max_attempts = 5
    for attempt in range(max_attempts):
        print(f"Attempt {attempt + 1} to initialize VideoWriter for {out_path}")
        out = cv2.VideoWriter(out_path, fourcc, 15, (1200, 1800))
        if out and out.isOpened():
            print("VideoWriter successfully initialized.")
            break
        time.sleep(1)
    if out is None or not out.isOpened():
        print("Failed to initialize video recording after several attempts.")
        cap.release()
        btn_start.config(state=tk.NORMAL)
        show_main_page()
        return

    recording = True
    threading.Thread(target=record_video, daemon=True).start()
    btn_stop.config(state=tk.NORMAL)

    if use_mic.get() and selected_mic.get():
        audio_path = os.path.join(SAVE_DIR, f"audio_{ts}.wav")
        dur = int(selected_duration.get()) + 3  # Длительность аудио: выбранное время + 3 секунды предзаписи
        audio_thread = threading.Thread(target=record_audio, args=(audio_path, dur, selected_mic.get()), daemon=True)
        audio_thread.start()
    else:
        audio_path = None
        print("Microphone recording skipped: either not enabled or no mic selected.")

    duration = int(selected_duration.get())
    start_recording_countdown(duration, stop_recording)

def record_video():
    global cap, recording, out
    target_frame_time = 1.0 / 15
    frame_buffer = []
    duration = int(selected_duration.get()) + 1  # Добавляем 1 секунду, чтобы учесть отображение 0
    start_time = time.time()
    
    while recording and cap and cap.isOpened():
        if time.time() - start_time > duration:
            break
            
        if out is None or not out.isOpened():
            print("Error: VideoWriter is None or not opened. Stopping recording thread.")
            break
        
        frame_start = time.time()
        ret, frame = cap.read()
        if not ret:
            print("Failed to read frame from camera. Stopping recording thread.")
            break
        
        rot = ROTATION_OPTIONS[selected_rotation.get()]
        if rot is not None:
            frame = cv2.rotate(frame, rot)
        
        h, w = frame.shape[:2]
        scale = 1200 / w
        nh = int(h * scale)
        frame = cv2.resize(frame, (1200, nh))
        if nh > 1800:
            off = (nh - 1800) // 2
            frame = frame[off:off+1800, :]
        else:
            pad = (1800 - nh) // 2
            frame = cv2.copyMakeBorder(frame, pad, 1800-nh-pad, 0, 0, cv2.BORDER_CONSTANT)
        
        frame_buffer.append(frame)
        
        if len(frame_buffer) >= 5:
            try:
                out.write(frame_buffer.pop(0))
            except Exception as e:
                print(f"Error writing frame to video: {e}")
                break
        
        elapsed = time.time() - frame_start
        sleep_time = max(0, target_frame_time - elapsed)
        time.sleep(sleep_time)
    
    while frame_buffer and out and out.isOpened():
        try:
            out.write(frame_buffer.pop(0))
        except Exception as e:
            print(f"Error writing remaining frames: {e}")
            break
    
    if out and out.isOpened():
        out.release()
        print("VideoWriter released successfully.")
    print("Recording thread stopped.")

def apply_overlay_to_video(input_path, output_path, overlay_img):
    temp_overlay_path = os.path.join(SAVE_DIR, "temp_overlay.png")
    cv2.imwrite(temp_overlay_path, overlay_img)

    cap = cv2.VideoCapture(input_path)
    if not cap.isOpened():
        print("Failed to open video for post-processing.")
        if os.path.exists(temp_overlay_path):
            os.remove(temp_overlay_path)
        return False

    fps = cap.get(cv2.CAP_PROP_FPS)
    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))

    cap.release()

    temp_output_path = os.path.join(SAVE_DIR, f"temp_output_{os.path.basename(output_path)}")
    try:
        result = subprocess.run([
            "ffmpeg", "-y",
            "-i", input_path,
            "-i", temp_overlay_path,
            "-filter_complex", f"[0:v][1:v]overlay=0:0[outv];[0:a]anull[outa]",
            "-map", "[outv]",
            "-map", "[outa]",
            "-c:v", "libx264",
            "-c:a", "aac",
            "-shortest",
            temp_output_path
        ], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)

        if result.returncode == 0:
            os.replace(temp_output_path, output_path)
            print(f"Overlay applied to video with audio: {output_path}")
            return True
        else:
            print(f"FFmpeg error: {result.stderr}")
            return False
    except Exception as e:
        print(f"Error applying overlay with audio: {e}")
        return False
    finally:
        if os.path.exists(temp_overlay_path):
            try:
                os.remove(temp_overlay_path)
            except Exception as e:
                print(f"Error deleting temporary overlay file {temp_overlay_path}: {e}")

        if os.path.exists(temp_output_path):
            max_attempts = 5
            for attempt in range(max_attempts):
                try:
                    os.remove(temp_output_path)
                    print(f"Temporary output file {temp_output_path} deleted.")
                    break
                except Exception as e:
                    print(f"Attempt {attempt + 1}: Error deleting temporary output file {temp_output_path}: {e}")
                    time.sleep(1)
            else:
                print(f"Failed to delete temporary output file {temp_output_path} after {max_attempts} attempts.")

def stop_recording():
    global recording, out, cap, countdown_active
    countdown_active = False  # Останавливаем отсчёт
    recording = False
    if out and out.isOpened():
        out.release()
        print("VideoWriter released in stop_recording.")
    out = None
    if cap and cap.isOpened():
        cap.release()
        cap = None
    stop_preview()
    btn_stop.config(state=tk.DISABLED)
    finalize_recording()

def finalize_recording():
    global last_uni_folder_id, preview_running, audio_path, audio_thread
    try:
        path = os.path.join(SAVE_DIR, recording_filename)
        if not os.path.exists(path) or os.path.getsize(path) == 0:
            print("Recording file not created or empty.")
            btn_start.config(state=tk.NORMAL)
            show_main_page()
            return

        if audio_thread is not None and audio_thread.is_alive():
            print("Waiting for audio recording to finish...")
            audio_thread.join()

        if use_mic.get() and audio_path and os.path.exists(audio_path):
            print(f"Merging audio from {audio_path} into video {path}")
            merged = os.path.join(SAVE_DIR, f"final_{recording_filename}")
            try:
                result = subprocess.run([
                    "ffmpeg", "-y",
                    "-i", path,
                    "-i", audio_path,
                    "-c:v", "copy",
                    "-c:a", "aac",
                    "-shortest",
                    merged
                ], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                if result.returncode == 0:
                    os.replace(merged, path)
                    print(f"Audio merged successfully into {path}")
                else:
                    print(f"FFmpeg error: {result.stderr}")
                    raise Exception("Failed to merge audio")
            except Exception as e:
                print(f"Error merging audio: {e}")
                if os.path.exists(path):
                    os.remove(path)
                btn_start.config(state=tk.NORMAL)
                show_main_page()
                return
            finally:
                if os.path.exists(audio_path):
                    max_attempts = 5
                    for attempt in range(max_attempts):
                        try:
                            os.remove(audio_path)
                            print(f"Temporary audio file {audio_path} deleted.")
                            break
                        except Exception as e:
                            print(f"Attempt {attempt + 1}: Error deleting temporary audio file {audio_path}: {e}")
                            time.sleep(1)
                    else:
                        print(f"Failed to delete temporary audio file {audio_path} after {max_attempts} attempts.")
                audio_path = None
                audio_thread = None

        if overlay_image_cv is not None:
            temp_path = os.path.join(SAVE_DIR, f"temp_{recording_filename}")
            os.rename(path, temp_path)
            success = apply_overlay_to_video(temp_path, path, overlay_image_cv)
            if success:
                max_attempts = 5
                for attempt in range(max_attempts):
                    try:
                        os.remove(temp_path)
                        print(f"Temporary video file {temp_path} deleted.")
                        break
                    except Exception as e:
                        print(f"Attempt {attempt + 1}: Error deleting temporary video file {temp_path}: {e}")
                        time.sleep(1)
                else:
                    print(f"Failed to delete temporary video file {temp_path} after {max_attempts} attempts.")
            else:
                os.rename(temp_path, path)
                print("Failed to apply overlay. Video saved without changes.")
                btn_start.config(state=tk.NORMAL)
                show_main_page()
                return

        ev_id = event_ids.get(selected_event.get())
        if ev_id:
            qr, url, uni_id = upload_to_drive(
                path, os.path.splitext(recording_filename)[0],
                ev_id, reuse_last=reuse_var.get(), last_folder_id=last_uni_folder_id
            )
            if qr is not None:
                last_uni_folder_id = uni_id
                show_result_page(path, qr)
            else:
                print("Failed to upload to Google Drive.")
                btn_start.config(state=tk.NORMAL)
                show_main_page()
                return
        else:
            print("Event not selected or unavailable.")
            btn_start.config(state=tk.NORMAL)
            show_main_page()
            return
        btn_start.config(state=tk.NORMAL)
    except Exception as e:
        print(f"Unexpected error during recording finalization: {e}")
        btn_start.config(state=tk.NORMAL)
        show_main_page()

def play_video(path, label):
    global video_loop_cap, video_loop_active
    if not os.path.exists(path) or os.path.getsize(path) == 0:
        print("Video file not found or empty.")
        return
    if video_loop_cap:
        video_loop_cap.release()
    video_loop_cap = cv2.VideoCapture(path)
    if not video_loop_cap.isOpened():
        print("Failed to open video for playback.")
        return

    instance = vlc.Instance()
    player = instance.media_player_new()
    media = instance.media_new(path)
    player.set_media(media)
    player.set_hwnd(label.winfo_id())
    player.play()
    print("Video and audio playback started with VLC.")

    video_loop_active = True
    def stream(player=player):
        global video_loop_active, video_loop_cap
        if not video_loop_active:
            player.stop()
            print("Video and audio playback stopped.")
            return
        ret, frame = video_loop_cap.read()
        if not ret:
            video_loop_cap.release()
            video_loop_cap = cv2.VideoCapture(path)
            if not video_loop_cap.isOpened():
                print("Failed to restart video playback.")
                video_loop_active = False
                player.stop()
                print("Video and audio playback stopped due to video restart failure.")
                return
            ret, frame = video_loop_cap.read()
            player.set_media(instance.media_new(path))
            player.play()
            print("Video and audio playback restarted.")
        if ret:
            h, w = frame.shape[:2]
            scale = 1200 / w
            nh = int(h * scale)
            fr = cv2.resize(frame, (1200, nh))
            if nh > 1800:
                off = (nh - 1800) // 2
                crop = fr[off:off+1800, :]
            else:
                pad = (1800 - nh) // 2
                crop = cv2.copyMakeBorder(fr, pad, 1800-nh-pad, 0, 0, cv2.BORDER_CONSTANT)
            ch, cw = crop.shape[:2]
            scale_w = 900 / cw
            scale_h = 1200 / ch
            s = min(scale_w, scale_h)
            pw, ph = int(cw * s), int(ch * s)
            resized = cv2.resize(crop, (pw, ph))
            rgb = cv2.cvtColor(resized, cv2.COLOR_BGR2RGB)
            imgtk = ImageTk.PhotoImage(Image.fromarray(rgb))
            label.imgtk = imgtk
            label.config(image=imgtk)
        label.after(30, stream)
    stream()

def show_settings_page():
    global preview_running, video_loop_active
    preview_running = False
    stop_preview()
    video_loop_active = False
    btn_stop.config(state=tk.DISABLED)
    main_page.pack_forget()
    settings_page.pack(fill=tk.BOTH, expand=True)

def stop_video_capture():
    global cap, video_loop_active, video_loop_cap
    if cap and cap.isOpened():
        cap.release()
    if video_loop_cap and video_loop_cap.isOpened():
        video_loop_cap.release()
    video_loop_active = False

def show_main_page():
    global preview_running, video_loop_active
    result_page.pack_forget()
    settings_page.pack_forget()
    main_page.pack(fill=tk.BOTH, expand=True)
    stop_video_capture()
    preview_running = True
    update_preview()
    btn_start.config(state=tk.NORMAL)
    btn_stop.config(state=tk.DISABLED)
    window.update()

def show_result_page(path, qr_img):
    settings_page.pack_forget()
    main_page.pack_forget()
    result_page.pack(fill=tk.BOTH, expand=True)
    video_label = tk.Label(result_page, bg="#000000")
    video_label.place(relx=0.5, rely=0.5, anchor="center", relwidth=1.0, relheight=1.0)
    play_video(path, video_label)
    back_button = ttk.Button(result_page, text="← Назад", style="SemiTransparent.TButton", command=show_main_page)
    back_button.place(relx=0.5, rely=0.05, anchor="center")
    if qr_img:
        qr_img_resized = qr_img.resize((200, 200))
        qr_array = np.array(qr_img_resized)
        if qr_array.dtype == bool:
            qr_array = qr_array.astype(np.uint8) * 255
        if qr_array.ndim == 2 or qr_array.shape[-1] == 1:
            qr_array = cv2.cvtColor(qr_array, cv2.COLOR_GRAY2RGBA) if qr_array.ndim == 2 else cv2.cvtColor(qr_array, cv2.COLOR_RGB2RGBA)
        if qr_array.shape[-1] == 3:
            alpha = np.full((200, 200, 1), 0, dtype=np.uint8)
            qr_array = np.dstack((qr_array, alpha))
        elif qr_array.shape[-1] == 4:
            qr_array[:, :, 3] = np.where(qr_array[:, :, :3].sum(axis=2) > 600, 255, 0)
        qr_img_with_alpha = Image.fromarray(qr_array)
        qr_tk = ImageTk.PhotoImage(qr_img_with_alpha)
        qr_label = tk.Label(result_page, image=qr_tk, bg="#000000")
        qr_label.image = qr_tk
        qr_label.place(relx=0.95, rely=0.95, anchor="se")

def toggle_fullscreen():
    window.attributes('-fullscreen', True)

def exit_fullscreen(event=None):
    window.attributes('-fullscreen', False)

window = tk.Tk()
window.title("Фотобудка")
window.geometry("900x1200")
window.configure(bg="#000000")
window.bind('<F11>', lambda e: show_settings_page())
window.bind('<Escape>', exit_fullscreen)

mic_devices = [d['name'] for d in sd.query_devices() if d['max_input_channels'] > 0]
selected_mic = tk.StringVar(window, value=mic_devices[0] if mic_devices else "")
use_mic = tk.BooleanVar(window, value=False)
selected_rotation = tk.StringVar(window, value="90° вправо (вертикально)")
selected_duration = tk.StringVar(window, value="5")
selected_event = tk.StringVar(window)
event_ids = {}
reuse_var = tk.BooleanVar(window, value=False)

settings_page = tk.Frame(window, bg="#000000")
main_page = tk.Frame(window, bg="#000000")
result_page = tk.Frame(window, bg="#000000")

style = ttk.Style()
style.theme_use('clam')
style.configure("Custom.TButton", background="#FF9800", foreground="white", font=("Helvetica", 40), padding=20)
style.map("Custom.TButton", background=[('active', '#F57C00')])
style.configure("SemiTransparent.TButton", background="#FFB74D", foreground="white", font=("Helvetica", 40), padding=20)
style.map("SemiTransparent.TButton", background=[('active', '#FFCC80')])
style.configure("Large.TCombobox",
    font=("Helvetica", 100),
    fieldbackground="#222222",
    background="#222222",
    foreground="white",
    arrowcolor="white",
    borderwidth=5,
    padding=10
)
style.map("Large.TCombobox",
    fieldbackground=[('readonly', '#222222'), ('!disabled', '#222222')],
    background=[('active', '#333333'), ('!disabled', '#222222')],
    foreground=[('active', 'white'), ('!disabled', 'white')],
    selectbackground=[('!disabled', '#555555')],
    selectforeground=[('!disabled', 'white')]
)
style.configure("Large.TCombobox*Listbox",
    font=("Helvetica", 120),
    background="#333333",
    foreground="white"
)
style.configure("Large.TCombobox*Listbox*Frame", background="#333333")
style.configure("Large.TCombobox*Listbox*Scrollbar",
    background="#333333",
    troughcolor="#222222"
)

settings_page.pack(fill=tk.BOTH, expand=True)
tk.Label(settings_page, text="Событие:", font=("Helvetica", 28), fg="white", bg="#000000").pack(pady=25)
combo_events = ttk.Combobox(settings_page, textvariable=selected_event, state="readonly", style="Modern.TCombobox")
combo_events.pack(pady=25)
ttk.Button(settings_page, text="⟳ Обновить", style="Custom.TButton", command=lambda: refresh_events()).pack(pady=25)
ttk.Button(settings_page, text="+ Новое событие", style="Custom.TButton", command=lambda: on_new_event()).pack(pady=25)
tk.Label(settings_page, text="Ориентация:", font=("Helvetica", 28), fg="white", bg="#000000").pack(pady=25)
for lbl in ROTATION_OPTIONS:
    tk.Radiobutton(settings_page, text=lbl, variable=selected_rotation, value=lbl, font=("Helvetica", 28), fg="white", bg="#000000", selectcolor="#000000").pack(anchor="w", pady=15)
ttk.Button(settings_page, text="Добавить рамку", style="Custom.TButton", command=load_overlay).pack(pady=25)
ttk.Button(settings_page, text="Убрать рамку", style="Custom.TButton", command=clear_overlay).pack(pady=25)
ttk.Button(settings_page, text="Открыть на весь экран", style="Custom.TButton", command=toggle_fullscreen).pack(pady=25)
tk.Checkbutton(
    settings_page,
    text="Использовать микрофон",
    variable=use_mic,
    font=("Helvetica", 28),
    fg="white", bg="#000000", selectcolor="#000000"
).pack(pady=20)
tk.Label(settings_page, text="Микрофон:", font=("Helvetica", 28), fg="white", bg="#000000").pack(pady=(10,0))
ttk.Combobox(
    settings_page,
    textvariable=selected_mic,
    values=mic_devices,
    state="readonly",
    style="Modern.TCombobox",
    font=("Helvetica", 28),
    width=30
).pack(pady=(0,25))
ttk.Button(settings_page, text="▶ Запустить", style="Custom.TButton", command=lambda: [settings_page.pack_forget(), show_main_page()]).pack(pady=50)

main_page.pack(fill=tk.BOTH, expand=True)
tk.Checkbutton(main_page, text="Добавить к предыдущему пользователю", variable=reuse_var, font=("Helvetica", 28), fg="white", bg="#000000", selectcolor="#000000", wraplength=1000, anchor="center").pack(pady=35)
tk.Label(main_page, text="Длительность (сек):", font=("Helvetica", 28), fg="white", bg="#000000").pack(pady=20)
options = ["5", "7", "10", "12", "15"]
om = tk.OptionMenu(
    main_page,
    selected_duration,
    *options
)
om.config(
    font=("Helvetica", 36),
    bg="#222222",
    fg="white",
    width=5,
    height=1,
    highlightthickness=0,
    bd=0,
    anchor="center",
    justify="center",
    activebackground="#333333",
    activeforeground="white",
    padx=5,
    pady=5
)
om["menu"].config(
    font=("Helvetica", 36),
    bg="#333333",
    fg="white",
    activebackground="#555555",
    activeforeground="white"
)
om.pack(pady=10, ipady=5)

preview_label = tk.Label(main_page, bg="#000000")
preview_label.place(relx=0.5, rely=0.5, anchor="center", relwidth=0.95, relheight=0.7)
btn_start = ttk.Button(main_page, text="▶", style="Custom.TButton", command=start_recording)
btn_start.place(relx=0.35, rely=0.95, anchor="center")
btn_stop = ttk.Button(main_page, text="■", style="Custom.TButton", state=tk.DISABLED, command=stop_recording)
btn_stop.place(relx=0.65, rely=0.95, anchor="center")

result_page.pack(fill=tk.BOTH, expand=True)

def refresh_events():
    names, ids = list_events()
    event_ids.clear()
    event_ids.update(ids)
    combo_events['values'] = names
    if names:
        selected_event.set(names[0])

def on_new_event():
    name = simpledialog.askstring("Новое событие", "Введите название:", parent=window)
    if name:
        event_id = create_event(name)
        if event_id:
            refresh_events()

refresh_events()
window.mainloop()