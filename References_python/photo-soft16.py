import tkinter as tk
from tkinter import messagebox, filedialog, simpledialog, ttk
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
overlay_image_path = None
overlay_image_cv = None
cap = None
preview_running = False
capturing = False
countdown_value = None
last_uni_folder_id = None
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
            media = MediaFileUpload(path, mimetype='image/png')
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
        if overlay_image_cv is not None and not format_a_var.get():
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
    global countdown_value
    def finish():
        global countdown_value
        countdown_value = None
        callback()
    def tick(n):
        global countdown_value
        countdown_value = n
        print(countdown_value)
        if n > 0:
            window.after(1000, lambda: tick(n-1))
        else:
            window.after(1000, finish)
    tick(sec)

def capture_photo():
    global cap
    if not (cap and cap.isOpened()):
        print("Camera not opened for capture.")
        return None
    ret, frame = cap.read()
    if not ret:
        print("Failed to capture frame.")
        return None
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
    return frame

def create_collage(photos, num_photos):
    # Целевой размер для печати (4x6 дюймов при 300 DPI)
    target_width = 1200
    target_height = 1800

    # Проверяем, включён ли формат А
    use_format_a = format_a_var.get()

    if num_photos == 1:
        photo = photos[0]
        if overlay_image_cv is not None and not use_format_a and selected_frame_mode.get() == "Наложить рамку на каждую фотку отдельно":
            ov = cv2.resize(overlay_image_cv, (photo.shape[1], photo.shape[0]))
            if ov.shape[2] == 4:
                alpha = ov[:, :, 3:] / 255.0
                rgb = ov[:, :, :3]
                photo = (photo * (1 - alpha) + rgb * alpha).astype(np.uint8)
        if overlay_image_cv is not None and not use_format_a and selected_frame_mode.get() == "Наложить рамку на итоговую картинку":
            ov = cv2.resize(overlay_image_cv, (target_width, target_height))
            if ov.shape[2] == 4:
                alpha = ov[:, :, 3:] / 255.0
                rgb = ov[:, :, :3]
                photo = (photo * (1 - alpha) + rgb * alpha).astype(np.uint8)
        return photo
    
    # Создаём пустой коллаж (чёрный фон)
    collage = np.zeros((target_height, target_width, 3), dtype=np.uint8)
    
    if use_format_a:
        # Формат А - две полоски фотографий
        # Базовый отступ между фотографиями в столбце
        base_spacing = 10
        
        # Отступ между фотографиями в столбце (в 2 раза больше базового)
        spacing_vertical = base_spacing * 2
        
        # Отступ по краям (в 2 раза меньше, чем между фотографиями)
        edge_spacing = base_spacing // 2
        
        # Отступ между двумя столбцами
        center_spacing = base_spacing
        
        # Высота нижнего пространства (пустого места для имитации 4 фото)
        bottom_space_height = int(target_height * 0.25)  # 25% от общей высоты
        
        # Отступ сверху (20% от нижнего пространства)
        top_padding = int(bottom_space_height * 0.2)
        
        # Доступная высота для фотографий
        available_height = target_height - bottom_space_height - top_padding
        
        # Рассчитываем ширину столбца (с учетом отступов)
        column_width = (target_width - edge_spacing * 2 - center_spacing) // 2
        
        # Количество фотографий в столбце (зависит от num_photos)
        photos_per_column = num_photos // 2
        
        # Высота одной фотографии
        photo_height = (available_height - (photos_per_column - 1) * spacing_vertical) // photos_per_column
        
        # Размещаем фотографии в двух столбцах
        for i, photo in enumerate(photos):
            # Обрезаем верхние 10% фотографии
            h, w = photo.shape[:2]
            crop_h = int(h * 0.1)
            photo = photo[crop_h:, :]
            
            # Масштабируем фото, сохраняя пропорции
            orig_h, orig_w = photo.shape[:2]
            scale = min(column_width / orig_w, photo_height / orig_h)
            new_width = int(orig_w * scale)
            new_height = int(orig_h * scale)
            resized = cv2.resize(photo, (new_width, new_height))
            
            # Определяем столбец и позицию в столбце
            column = i // photos_per_column
            pos_in_column = i % photos_per_column
            
            # Координаты для размещения фото
            x_start = int(edge_spacing + column * (column_width + center_spacing))
            y_start = int(top_padding + pos_in_column * (photo_height + spacing_vertical))
            
            # Центрируем фото в ячейке
            x_offset = int((column_width - new_width) // 2)
            y_offset = int((photo_height - new_height) // 2)
            
            # Убедимся, что все индексы целочисленные
            y_start_pos = int(y_start + y_offset)
            y_end_pos = int(y_start_pos + new_height)
            x_start_pos = int(x_start + x_offset)
            x_end_pos = int(x_start_pos + new_width)
            
            # Проверяем, что индексы в пределах размеров коллажа
            if (y_end_pos <= target_height and x_end_pos <= target_width and
                y_start_pos >= 0 and x_start_pos >= 0):
                # Размещаем фото в коллаже
                collage[y_start_pos:y_end_pos, x_start_pos:x_end_pos] = resized
        
        return collage
    
    # Если не формат А, используем обычный режим
    # Базовый отступ между фотографиями
    base_spacing = 2  # Очень маленький базовый отступ
    
    # Отступ между фотографиями (увеличенный в 3 раза)
    spacing = base_spacing * 3
    
    # Отступ от краев (очень маленький)
    edge_spacing = 1

    # Отступ сверху (равен 4 отступам между фото)
    top_padding = spacing * 4

    # Определяем размеры коллажа и фотографии с учётом целевого размера 1200x1800
    if num_photos == 4:
        rows, cols = 2, 2
        # Вычисляем размер фото
        photo_width = (target_width - (edge_spacing * 2) - spacing) // cols
        photo_height = (target_height - top_padding - spacing) // rows
    else:  # num_photos == 6
        rows, cols = 3, 2
        # Вычисляем размер фото
        photo_width = (target_width - (edge_spacing * 2) - spacing) // cols
        photo_height = (target_height - top_padding - (spacing * 2)) // rows

    for i, photo in enumerate(photos):
        # Обрезаем верхние 10% фотографии
        h, w = photo.shape[:2]
        crop_h = int(h * 0.1)  # 10% от высоты
        photo = photo[crop_h:, :]  # Удаляем верхнюю часть

        # Масштабируем фото, сохраняя пропорции, без обрезки
        orig_h, orig_w = photo.shape[:2]
        scale = min(photo_width / orig_w, photo_height / orig_h)
        new_width = int(orig_w * scale)
        new_height = int(orig_h * scale)
        resized = cv2.resize(photo, (new_width, new_height))

        # Наложение рамки на каждую фотографию (режим 1), только если не формат А
        if overlay_image_cv is not None and not use_format_a and selected_frame_mode.get() == "Наложить рамку на каждую фотку отдельно":
            ov = cv2.resize(overlay_image_cv, (new_width, new_height))
            if ov.shape[2] == 4:
                alpha = ov[:, :, 3:] / 255.0
                rgb = ov[:, :, :3]
                resized = (resized * (1 - alpha) + rgb * alpha).astype(np.uint8)

        # Размещаем фото в коллаже с учётом пробелов
        row = i // cols
        col = i % cols
        
        # Координаты для размещения фото
        x_start = int(edge_spacing + col * (photo_width + spacing))
        y_start = int(top_padding + row * (photo_height + spacing))

        # Центрируем фото в ячейке
        x_offset = int((photo_width - new_width) // 2)
        y_offset = int((photo_height - new_height) // 2)
        
        # Убедимся, что все индексы целочисленные
        y_start_pos = int(y_start + y_offset)
        y_end_pos = int(y_start_pos + new_height)
        x_start_pos = int(x_start + x_offset)
        x_end_pos = int(x_start_pos + new_width)
            
        # Проверяем, что индексы в пределах размеров коллажа
        if (y_end_pos <= target_height and x_end_pos <= target_width and
            y_start_pos >= 0 and x_start_pos >= 0):
            # Размещаем фото в коллаже
            collage[y_start_pos:y_end_pos, x_start_pos:x_end_pos] = resized

    # Наложение рамки на каждый столбец (режим 3), только если не формат А
    if overlay_image_cv is not None and not use_format_a and selected_frame_mode.get() == "Наложить рамку на каждый столбец":
        column_height = target_height - top_padding
        for col_idx in range(cols):
            x_start = int(edge_spacing + col_idx * (photo_width + spacing))
            ov = cv2.resize(overlay_image_cv, (photo_width, column_height))
            if ov.shape[2] == 4:
                alpha = ov[:, :, 3:] / 255.0
                rgb = ov[:, :, :3]
                y_start_pos = int(top_padding)
                y_end_pos = int(target_height)
                x_start_pos = int(x_start)
                x_end_pos = int(x_start + photo_width)
                
                if (y_end_pos <= target_height and x_end_pos <= target_width and
                    y_start_pos >= 0 and x_start_pos >= 0):
                    collage[y_start_pos:y_end_pos, x_start_pos:x_end_pos] = (
                        collage[y_start_pos:y_end_pos, x_start_pos:x_end_pos] * (1 - alpha) +
                        rgb * alpha
                    ).astype(np.uint8)

    # Наложение рамки на итоговую картинку (режим 2), только если не формат А
    if overlay_image_cv is not None and not use_format_a and selected_frame_mode.get() == "Наложить рамку на итоговую картинку":
        ov = cv2.resize(overlay_image_cv, (target_width, target_height))
        if ov.shape[2] == 4:
            alpha = ov[:, :, 3:] / 255.0
            rgb = ov[:, :, :3]
            collage = (collage * (1 - alpha) + rgb * alpha).astype(np.uint8)
        else:
            collage = cv2.addWeighted(collage, 0.7, ov, 0.3, 0)

    return collage

def start_capture():
    global capturing, cap
    btn_start.config(state=tk.DISABLED)
    num_photos = int(selected_photos.get())
    capturing = True
    photos = []

    cap = cv2.VideoCapture(camera_index, cv2.CAP_DSHOW)
    if not cap.isOpened():
        print("Failed to open camera for capture.")
        btn_start.config(state=tk.NORMAL)
        capturing = False
        show_main_page()
        return
    cap.set(cv2.CAP_PROP_FPS, 30)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1200)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 1800)

    def capture_next(i):
        global capturing, cap
        if i >= num_photos or not capturing:
            if cap is not None:
                cap.release()
                cap = None
            capturing = False
            if photos:
                finalize_photos(photos, num_photos)
            else:
                btn_start.config(state=tk.NORMAL)
                show_main_page()
            return
        start_countdown(3, lambda: take_photo(i, capture_next))

    def take_photo(i, next_callback):
        global capturing
        if not capturing:
            return
        photo = capture_photo()
        if photo is not None:
            photos.append(photo)
            print(f"Photo {i+1}/{num_photos} captured.")
        else:
            print(f"Failed to capture photo {i+1}.")
        next_callback(i + 1)

    capture_next(0)

def stop_capture():
    global capturing
    capturing = False
    btn_start.config(state=tk.NORMAL)

def finalize_photos(photos, num_photos):
    global last_uni_folder_id
    ts = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
    filename = f"photo_{ts}.png"
    path = os.path.join(SAVE_DIR, filename)

    collage = create_collage(photos, num_photos)
    cv2.imwrite(path, collage)
    print(f"Photo(s) saved to {path}")

    ev_id = event_ids.get(selected_event.get())
    if ev_id:
        qr, url, uni_id = upload_to_drive(
            path, os.path.splitext(filename)[0],
            ev_id, reuse_last=reuse_var.get(), last_folder_id=last_uni_folder_id
        )
        if qr is not None:
            last_uni_folder_id = uni_id
            show_result_page(path, qr)
        else:
            print("Failed to upload to Google Drive.")
            btn_start.config(state=tk.NORMAL)
            show_main_page()
    else:
        print("Event not selected or unavailable.")
        btn_start.config(state=tk.NORMAL)
        show_main_page()

def print_image(path):
    try:
        os.startfile(path, "print")
        print(f"Sent {path} to printer.")
    except Exception as e:
        print(f"Failed to print: {e}")

def show_settings_page():
    global preview_running
    preview_running = False
    stop_preview()
    main_page.pack_forget()
    settings_page.pack(fill=tk.BOTH, expand=True)

def stop_camera():
    global cap, preview_running
    if cap and cap.isOpened():
        cap.release()
    preview_running = False

def show_main_page():
    global preview_running
    result_page.pack_forget()
    settings_page.pack_forget()
    main_page.pack(fill=tk.BOTH, expand=True)
    stop_camera()
    preview_running = True
    update_preview()
    btn_start.config(state=tk.NORMAL)
    window.update()

def show_result_page(path, qr_img):
    settings_page.pack_forget()
    main_page.pack_forget()
    result_page.pack(fill=tk.BOTH, expand=True)
    img = cv2.imread(path)
    if img is None:
        print(f"Failed to load image from {path} for display.")
        btn_start.config(state=tk.NORMAL)
        show_main_page()
        return
    h, w = img.shape[:2]
    scale = 900 / w
    nh = int(h * scale)
    resized = cv2.resize(img, (900, nh))
    rgb = cv2.cvtColor(resized, cv2.COLOR_BGR2RGB)
    imgtk = ImageTk.PhotoImage(Image.fromarray(rgb))
    photo_label = tk.Label(result_page, image=imgtk, bg="#000000")
    photo_label.image = imgtk
    photo_label.place(relx=0.5, rely=0.5, anchor="center")
    back_button = ttk.Button(result_page, text="← Назад", style="SemiTransparent.TButton", command=show_main_page)
    back_button.place(relx=0.5, rely=0.05, anchor="center")
    print_button = ttk.Button(result_page, text="🖨 Печать", style="SemiTransparent.TButton", command=lambda: print_image(path))
    print_button.place(relx=0.5, rely=0.95, anchor="center")
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

selected_rotation = tk.StringVar(window, value="90° вправо (вертикально)")
selected_photos = tk.StringVar(window, value="1")
selected_event = tk.StringVar(window)
event_ids = {}
reuse_var = tk.BooleanVar(window, value=False)
selected_frame_mode = tk.StringVar(window, value="Наложить рамку на каждую фотку отдельно")
format_a_var = tk.BooleanVar(window, value=False)

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
tk.Label(settings_page, text="Режим рамки:", font=("Helvetica", 28), fg="white", bg="#000000").pack(pady=25)
combo_frame_mode = ttk.Combobox(settings_page, textvariable=selected_frame_mode, state="readonly", values=[
    "Наложить рамку на каждую фотку отдельно",
    "Наложить рамку на итоговую картинку",
    "Наложить рамку на каждый столбец"
], style="Modern.TCombobox")
combo_frame_mode.pack(pady=25)
ttk.Button(settings_page, text="Добавить рамку", style="Custom.TButton", command=load_overlay).pack(pady=25)
ttk.Button(settings_page, text="Убрать рамку", style="Custom.TButton", command=clear_overlay).pack(pady=25)
tk.Checkbutton(settings_page, text="Сделать по формату А", variable=format_a_var, font=("Helvetica", 28), fg="white", bg="#000000", selectcolor="#000000").pack(pady=25)
ttk.Button(settings_page, text="Открыть на весь экран", style="Custom.TButton", command=toggle_fullscreen).pack(pady=25)
ttk.Button(settings_page, text="▶ Запустить", style="Custom.TButton", command=lambda: [settings_page.pack_forget(), show_main_page()]).pack(pady=50)

main_page.pack(fill=tk.BOTH, expand=True)
tk.Checkbutton(main_page, text="Добавить к предыдущему пользователю", variable=reuse_var, font=("Helvetica", 28), fg="white", bg="#000000", selectcolor="#000000", wraplength=1000, anchor="center").pack(pady=35)
tk.Label(main_page, text="Количество фото:", font=("Helvetica", 28), fg="white", bg="#000000").pack(pady=20)
options = ["1", "4", "6"]
om = tk.OptionMenu(
    main_page,
    selected_photos,
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
btn_start = ttk.Button(main_page, text="▶", style="Custom.TButton", command=start_capture)
btn_start.place(relx=0.5, rely=0.95, anchor="center")

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