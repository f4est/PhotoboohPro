import tkinter as tk
from tkinter import messagebox, filedialog, simpledialog, ttk
from threading import Thread
import datetime
import os
import qrcode
import cv2
import numpy as np
from PIL import Image, ImageTk, ImageDraw, ImageFont
from google.oauth2 import service_account
from googleapiclient.discovery import build
from googleapiclient.http import MediaFileUpload
import queue
import threading
import time
import sounddevice as sd
import soundfile as sf
import json
import platform
import subprocess

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

SAVE_DIR = os.path.abspath("photos")
os.makedirs(SAVE_DIR, exist_ok=True)

DEVICE_NAME = "EOS Webcam Utility"
camera_index = 0
frame_template_path = None
frame_template_cv = None
photo_positions = []  # Позиции для вставки фотографий (x, y, width, height)
cap = None
preview_running = False
current_photo = 0
captured_photos = []
countdown_value = None
photo_session_active = False
last_uni_folder_id = None
mirror_mode = False  # Переменная для режима зеркального отображения

FRAME_WIDTH = 1200
FRAME_HEIGHT = 1800

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
            media = MediaFileUpload(path, mimetype='image/jpeg')
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

def load_frame_template():
    global frame_template_path, frame_template_cv, photo_positions
    file = filedialog.askopenfilename(filetypes=[("Image Files", "*.png;*.jpg;*.jpeg")])
    if not file:
        return
    
    img = cv2.imread(file, cv2.IMREAD_UNCHANGED)
    if img is None:
        print("Failed to load frame template.")
        return
    
    frame_template_path = file
    frame_template_cv = cv2.resize(img, (FRAME_WIDTH, FRAME_HEIGHT))
    
    auto_detect_photo_positions()
    print("Frame template loaded successfully.")

def auto_detect_photo_positions():
    global photo_positions, frame_template_cv
    if frame_template_cv is None:
        return
    
    photo_width, photo_height = 280, 210
    photo_positions = [
        (50, 50, photo_width, photo_height),
        (870, 50, photo_width, photo_height),
        (50, 1540, photo_width, photo_height),
        (870, 1540, photo_width, photo_height)
    ]
    print(f"Auto-detected photo positions: {photo_positions}")

def setup_photo_positions():
    global photo_positions, frame_template_cv
    if frame_template_cv is None:
        messagebox.showwarning("Предупреждение", "Сначала загрузите рамку")
        return
    
    if not photo_positions:
        auto_detect_photo_positions()
    
    dragging = False
    resizing = False
    resize_mode = None
    selected_idx = -1
    start_x, start_y = 0, 0
    initial_pos = None

    def on_mouse(event, x, y, flags, param):
        nonlocal dragging, resizing, resize_mode, selected_idx, start_x, start_y, initial_pos
        
        if event == cv2.EVENT_LBUTTONDOWN:
            for i, (px, py, pw, ph) in enumerate(photo_positions):
                if px <= x <= px + pw and py <= y <= py + ph:
                    selected_idx = i
                    start_x, start_y = x, y
                    initial_pos = (px, py, pw, ph)
                    if abs(x - px) < 10 and abs(y - py) < 10:
                        resize_mode = 'corner'
                        resizing = True
                    elif abs(x - (px + pw)) < 10 and abs(y - (py + ph)) < 10:
                        resize_mode = 'corner'
                        resizing = True
                    elif abs(x - px) < 10 or abs(x - (px + pw)) < 10:
                        resize_mode = 'width'
                        resizing = True
                    elif abs(y - py) < 10 or abs(y - (py + ph)) < 10:
                        resize_mode = 'height'
                        resizing = True
                    else:
                        dragging = True
                    break
            if selected_idx == -1 and len(photo_positions) < 4:
                photo_positions.append((x, y, 280, 210))
                selected_idx = len(photo_positions) - 1
                initial_pos = (x, y, 280, 210)
                dragging = True

        elif event == cv2.EVENT_MOUSEMOVE and (dragging or resizing):
            dx, dy = x - start_x, y - start_y
            px, py, pw, ph = initial_pos
            if resizing:
                if resize_mode == 'corner':
                    new_width = max(100, pw + dx)
                    new_height = max(100, ph + dy)
                    scale = min(new_width / pw, new_height / ph)
                    new_width = int(pw * scale)
                    new_height = int(ph * scale)
                    photo_positions[selected_idx] = (px, py, new_width, new_height)
                elif resize_mode == 'width':
                    new_width = max(100, pw + dx)
                    photo_positions[selected_idx] = (px, py, new_width, ph)
                elif resize_mode == 'height':
                    new_height = max(100, ph + dy)
                    photo_positions[selected_idx] = (px, py, pw, new_height)
            elif dragging:
                new_x = max(0, min(px + dx, FRAME_WIDTH - pw))
                new_y = max(0, min(py + dy, FRAME_HEIGHT - ph))
                photo_positions[selected_idx] = (new_x, new_y, pw, ph)

            temp_img = frame_template_cv.copy()
            for i, (px, py, pw, ph) in enumerate(photo_positions):
                cv2.rectangle(temp_img, (px, py), (px + pw, py + ph), (0, 255, 0), 2)
                cv2.putText(temp_img, f"{i+1}", (px + 10, py + 30), 
                           cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
                cv2.circle(temp_img, (px, py), 5, (0, 0, 255), -1)
                cv2.circle(temp_img, (px + pw, py + ph), 5, (0, 0, 255), -1)
            cv2.imshow('Setup Positions', temp_img)

        elif event == cv2.EVENT_LBUTTONUP:
            dragging = False
            resizing = False
            resize_mode = None
            selected_idx = -1
            initial_pos = None

        elif event == cv2.EVENT_RBUTTONDOWN and len(photo_positions) > 0:
            if selected_idx >= 0:
                width = simpledialog.askinteger("Ширина", "Введите ширину (пиксели):", 
                                               parent=window, initialvalue=photo_positions[selected_idx][2], minvalue=100)
                height = simpledialog.askinteger("Высота", "Введите высоту (пиксели):", 
                                                parent=window, initialvalue=photo_positions[selected_idx][3], minvalue=100)
                if width and height:
                    px, py, _, _ = photo_positions[selected_idx]
                    photo_positions[selected_idx] = (px, py, width, height)
            temp_img = frame_template_cv.copy()
            for i, (px, py, pw, ph) in enumerate(photo_positions):
                cv2.rectangle(temp_img, (px, py), (px + pw, py + ph), (0, 255, 0), 2)
                cv2.putText(temp_img, f"{i+1}", (px + 10, py + 30), 
                           cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
            cv2.imshow('Setup Positions', temp_img)

    temp_img = frame_template_cv.copy()
    cv2.imshow('Setup Positions', temp_img)
    cv2.setMouseCallback('Setup Positions', on_mouse)
    
    print("ЛКМ: установка/перемещение позиции или изменение размера (углы/стороны).")
    print("ПКМ: изменить размеры вручную. Нажмите любую клавишу для завершения.")
    cv2.waitKey(0)
    cv2.destroyAllWindows()
    
    if len(photo_positions) != 4:
        messagebox.showwarning("Внимание", f"Установлено только {len(photo_positions)} позиций из 4")

def clear_frame_template():
    global frame_template_path, frame_template_cv, photo_positions
    frame_template_path = None
    frame_template_cv = None
    photo_positions = []
    print("Frame template cleared.")

def toggle_mirror_mode():
    global mirror_mode
    mirror_mode = not mirror_mode
    mirror_button.config(text="🔄 Отзеркалить (Вкл)" if mirror_mode else "🔄 Отзеркалить (Выкл)")
    print(f"Mirror mode {'enabled' if mirror_mode else 'disabled'}")

preview_queue = queue.Queue(maxsize=10)

def process_frames():
    global cap, countdown_value, mirror_mode
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
        scale = 1350 / w
        nh = int(h * scale)
        frame = cv2.resize(frame, (1350, nh))
        
        # Применяем зеркальное отображение для превью
        if mirror_mode:
            frame = cv2.flip(frame, 1)
        
        if countdown_value is not None:
            txt = str(countdown_value)
            org = (frame.shape[1]//2 - 60, frame.shape[0]//2 + 60)
            cv2.putText(frame, txt, org,
                        cv2.FONT_HERSHEY_SIMPLEX, 5,
                        (255, 255, 255), 10, cv2.LINE_AA)
        
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
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1920)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 1080)
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
            if photo_session_active:
                photo_counter_label.config(text=f"{current_photo + 1}/4")
            else:
                photo_counter_label.config(text="")
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
    photo_counter_label.config(text="")

def start_countdown(sec, callback):
    global countdown_value
    print(f"Starting countdown for photo {current_photo + 1}")
    def tick(n):
        global countdown_value
        countdown_value = n
        print(f"Countdown: {countdown_value}")
        if n > 0:
            window.after(1000, lambda: tick(n-1))
        else:
            countdown_value = None
            callback()
    
    countdown_value = sec
    tick(sec)

def capture_photo():
    global cap, mirror_mode
    if not (cap and cap.isOpened()):
        print("Camera not available")
        return None
    
    ret, frame = cap.read()
    if not ret:
        print("Failed to capture photo")
        return None
    
    rot = ROTATION_OPTIONS[selected_rotation.get()]
    if rot is not None:
        frame = cv2.rotate(frame, rot)
    
    # Применяем зеркальное отображение к захваченному фото
    if mirror_mode:
        frame = cv2.flip(frame, 1)
    
    return frame

def take_next_photo():
    global current_photo, captured_photos, photo_session_active
    
    if not photo_session_active or current_photo >= 4:
        print(f"Stopping photo session at {current_photo}/4")
        if current_photo >= 4:
            finalize_photo_session()
        return
    
    print(f"Taking photo {current_photo + 1}/4")
    
    def capture_callback():
        global current_photo, captured_photos
        photo = capture_photo()
        if photo is not None:
            captured_photos.append(photo)
            current_photo += 1
            print(f"Captured photo {current_photo}/4")
            if current_photo < 4:
                window.after(500, take_next_photo)
            else:
                finalize_photo_session()
    
    start_countdown(3, capture_callback)

def start_photo_session():
    global current_photo, captured_photos, photo_session_active
    
    current_photo = 0
    captured_photos = []
    photo_session_active = True
    
    btn_start.config(state=tk.DISABLED)
    print("Starting photo session")
    take_next_photo()

def create_final_collage():
    global captured_photos, frame_template_cv, photo_positions, mirror_mode
    
    if len(captured_photos) != 4:
        print(f"Not enough photos: {len(captured_photos)}/4")
        return None
    
    if frame_template_cv is None:
        final_image = np.ones((FRAME_HEIGHT, FRAME_WIDTH, 3), dtype=np.uint8) * 255
    else:
        final_image = frame_template_cv.copy()
    
    if len(photo_positions) != 4:
        print("Photo positions not set properly")
        return None
    
    for i, (photo, (x, y, w, h)) in enumerate(zip(captured_photos, photo_positions)):
        ph, pw = photo.shape[:2]
        target_aspect = w / h
        current_aspect = pw / ph
        
        if current_aspect > target_aspect:
            new_width = int(ph * target_aspect)
            offset = (pw - new_width) // 2
            cropped_photo = photo[:, offset:offset + new_width]
        else:
            new_height = int(pw / target_aspect)
            offset = (ph - new_height) // 2
            cropped_photo = photo[offset:offset + new_height, :]
        
        resized_photo = cv2.resize(cropped_photo, (w, h))
        
        if x + w <= final_image.shape[1] and y + h <= final_image.shape[0]:
            if frame_template_cv is not None and frame_template_cv.shape[2] == 4:
                alpha_region = frame_template_cv[y:y+h, x:x+w, 3] / 255.0
                for c in range(3):
                    final_image[y:y+h, x:x+w, c] = \
                        (1 - alpha_region) * resized_photo[:, :, c] + \
                        alpha_region * final_image[y:y+h, x:x+w, c]
            else:
                final_image[y:y+h, x:x+w] = resized_photo
    
    # Убрано зеркальное отображение итогового коллажа, так как фото уже отзеркалены на этапе захвата
    return final_image

def finalize_photo_session():
    global photo_session_active, last_uni_folder_id, current_photo, captured_photos
    
    photo_session_active = False
    btn_start.config(state=tk.NORMAL)
    print("Finalizing photo session")
    
    if len(captured_photos) != 4:
        print("Photo session incomplete")
        show_main_page()
        return
    
    final_collage = create_final_collage()
    if final_collage is None:
        print("Failed to create collage")
        show_main_page()
        return
    
    timestamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    filename = f"collage_{timestamp}.jpg"
    filepath = os.path.join(SAVE_DIR, filename)
    
    cv2.imwrite(filepath, final_collage)
    print(f"Collage saved: {filepath}")
    
    ev_id = event_ids.get(selected_event.get())
    if ev_id:
        qr, url, uni_id = upload_to_drive(
            filepath, f"collage_{timestamp}",
            ev_id, reuse_last=reuse_var.get(), last_folder_id=last_uni_folder_id
        )
        if qr is not None:
            last_uni_folder_id = uni_id
            show_result_page(filepath, qr)
        else:
            print("Failed to upload to Google Drive.")
            show_main_page()
    else:
        print("Event not selected.")
        show_main_page()

def print_image(filepath):
    try:
        if platform.system() == "Windows":
            os.startfile(filepath, "print")
        else:
            subprocess.run(["lp", filepath])
        print("Image sent to printer.")
    except Exception as e:
        messagebox.showerror("Ошибка печати", f"Не удалось отправить на печать: {e}")

def show_result_page(image_path, qr_img):
    settings_page.pack_forget()
    main_page.pack_forget()
    result_page.pack(fill=tk.BOTH, expand=True)
    
    for widget in result_page.winfo_children():
        widget.destroy()
    
    try:
        img = cv2.imread(image_path)
        if img is not None:
            h, w = img.shape[:2]
            max_height = 1600
            scale = max_height / h
            new_w = int(w * scale)
            new_h = int(h * scale)
            
            img_resized = cv2.resize(img, (new_w, new_h))
            img_rgb = cv2.cvtColor(img_resized, cv2.COLOR_BGR2RGB)
            img_pil = Image.fromarray(img_rgb)
            img_tk = ImageTk.PhotoImage(img_pil)
            
            img_label = tk.Label(result_page, image=img_tk, bg="#000000")
            img_label.image = img_tk
            img_label.pack(pady=20)
    except Exception as e:
        print(f"Error displaying result image: {e}")
    
    print_button = ttk.Button(result_page, text="🖨️ Печать", 
                             style="Custom.TButton", command=lambda: print_image(image_path))
    print_button.pack(pady=10)
    
    back_button = ttk.Button(result_page, text="← Назад", 
                            style="Custom.TButton", command=show_main_page)
    back_button.pack(pady=10)
    
    if qr_img:
        try:
            qr_img_resized = qr_img.resize((200, 200))
            qr_tk = ImageTk.PhotoImage(qr_img_resized)
            qr_label = tk.Label(result_page, image=qr_tk, bg="#000000")
            qr_label.image = qr_tk
            qr_label.pack(pady=10)
        except Exception as e:
            print(f"Error displaying QR code: {e}")

def show_settings_page():
    global preview_running
    preview_running = False
    stop_preview()
    main_page.pack_forget()
    result_page.pack_forget()
    settings_page.pack(fill=tk.BOTH, expand=True)

def show_main_page():
    global preview_running, current_photo, captured_photos, photo_session_active
    result_page.pack_forget()
    settings_page.pack_forget()
    main_page.pack(fill=tk.BOTH, expand=True)
    
    photo_session_active = False
    current_photo = 0
    captured_photos = []
    
    stop_preview()
    preview_running = True
    update_preview()
    btn_start.config(state=tk.NORMAL)

def toggle_fullscreen():
    window.attributes('-fullscreen', True)

def exit_fullscreen(event=None):
    window.attributes('-fullscreen', False)

window = tk.Tk()
window.title("Фотобудка - 4 фото")
window.geometry("900x1200")
window.configure(bg="#000000")
window.bind('<F11>', lambda e: show_settings_page())
window.bind('<Escape>', exit_fullscreen)

selected_rotation = tk.StringVar(window, value="90° вправо (вертикально)")
selected_event = tk.StringVar(window)
event_ids = {}
reuse_var = tk.BooleanVar(window, value=False)

settings_page = tk.Frame(window, bg="#000000")
main_page = tk.Frame(window, bg="#000000")
result_page = tk.Frame(window, bg="#000000")

style = ttk.Style()
style.theme_use('clam')
style.configure("Custom.TButton", background="#FF9800", foreground="white", 
                font=("Helvetica", 40), padding=20)
style.map("Custom.TButton", background=[('active', '#F57C00')])

settings_page.pack(fill=tk.BOTH, expand=True)

tk.Label(settings_page, text="НАСТРОЙКИ ФОТОБУДКИ", 
         font=("Helvetica", 32), fg="white", bg="#000000").pack(pady=30)

tk.Label(settings_page, text="Событие:", 
         font=("Helvetica", 28), fg="white", bg="#000000").pack(pady=15)
combo_events = ttk.Combobox(settings_page, textvariable=selected_event, 
                           state="readonly", font=("Helvetica", 20))
combo_events.pack(pady=10)

ttk.Button(settings_page, text="⟳ Обновить события", 
           style="Custom.TButton", command=lambda: refresh_events()).pack(pady=15)

ttk.Button(settings_page, text="+ Новое событие", 
           style="Custom.TButton", command=lambda: on_new_event()).pack(pady=15)

tk.Label(settings_page, text="Ориентация камеры:", 
         font=("Helvetica", 28), fg="white", bg="#000000").pack(pady=25)

for lbl in ROTATION_OPTIONS:
    tk.Radiobutton(settings_page, text=lbl, variable=selected_rotation, value=lbl,
                   font=("Helvetica", 24), fg="white", bg="#000000", 
                   selectcolor="#000000").pack(anchor="w", pady=8)

ttk.Button(settings_page, text="Загрузить рамку", 
           style="Custom.TButton", command=load_frame_template).pack(pady=20)

ttk.Button(settings_page, text="Настроить позиции фото", 
           style="Custom.TButton", command=setup_photo_positions).pack(pady=15)

ttk.Button(settings_page, text="Убрать рамку", 
           style="Custom.TButton", command=clear_frame_template).pack(pady=15)

# Добавляем кнопку "Отзеркалить"
mirror_button = ttk.Button(settings_page, text="🔄 Отзеркалить (Выкл)", 
                           style="Custom.TButton", command=toggle_mirror_mode)
mirror_button.pack(pady=15)

ttk.Button(settings_page, text="Полный экран", 
           style="Custom.TButton", command=toggle_fullscreen).pack(pady=20)

ttk.Button(settings_page, text="▶ ЗАПУСТИТЬ ФОТОБУДКУ", 
           style="Custom.TButton", command=show_main_page).pack(pady=40)

main_page.pack(fill=tk.BOTH, expand=True)

tk.Label(main_page, text="ФОТОБУДКА", font=("Helvetica", 48), 
         fg="white", bg="#000000").pack(pady=5)

tk.Checkbutton(main_page, text="Добавить к предыдущему пользователю", 
               variable=reuse_var, font=("Helvetica", 24), 
               fg="white", bg="#000000", selectcolor="#000000").pack(pady=5)

tk.Label(main_page, text="Будет сделано 4 фотографии", 
         font=("Helvetica", 28), fg="yellow", bg="#000000").pack(pady=5)

photo_counter_label = tk.Label(main_page, text="", font=("Helvetica", 40), 
                               fg="white", bg="#000000")
photo_counter_label.pack(pady=10)

preview_label = tk.Label(main_page, bg="#000000")
preview_label.place(relx=0.5, rely=0.55, anchor="center", relwidth=1.0, relheight=0.75)

btn_start = ttk.Button(main_page, text="📸 НАЧАТЬ ФОТОСЕССИЮ", 
                       style="Custom.TButton", command=start_photo_session)
btn_start.place(relx=0.5, rely=0.9, anchor="center")

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

if __name__ == "__main__":
    window.mainloop()