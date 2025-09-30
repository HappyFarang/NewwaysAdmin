# File: unified_video_detection.py
# Purpose: Face detection and recognition with stdin command handling
# FIXED: A) No names in idle B) Best match selection C) Proper stdin commands

import cv2
import base64
import json
import sys
import time
import os
import select
import numpy as np
import face_recognition

class VideoDetectionService:
    def __init__(self):
        self.cap = None
        self.face_cascade = None
        self.running = False
        self.detection_mode = "idle"
        self.detection_count = 0
        self.detection_threshold = 5
        self.recognition_result = None
        
        # Face recognition data
        self.workers = []
        self.recognition_enabled = False
        
        # Strict thresholds
        self.min_confidence_distance = 0.45  # 55% minimum confidence
        self.ambiguity_threshold = 0.15      # 15% gap between 1st and 2nd
        
    def send_message(self, msg_type, **kwargs):
        """Send JSON message to C#"""
        message = {"type": msg_type, "timestamp": time.time(), **kwargs}
        print(json.dumps(message))
        sys.stdout.flush()
        
    def load_workers(self):
        """Load worker face encodings from JSON files"""
        try:
            workers_folder = r"C:\NewwaysAdmin\WorkerAttendance"
            self.workers = []
                        
            if not os.path.exists(workers_folder):
                self.send_message("error", message=f"Workers folder not found: {workers_folder}")
                return False
            
            worker_count = 0
            json_files = [f for f in os.listdir(workers_folder) if f.endswith('.json')]
            
            for filename in json_files:
                filepath = os.path.join(workers_folder, filename)
                try:
                    with open(filepath, 'r') as f:
                        worker_data = json.load(f)
                        
                    if not worker_data.get('IsActive', True):
                        continue
                        
                    face_encodings = []
                    for encoding_b64 in worker_data.get('FaceEncodings', []):
                        encoding_bytes = base64.b64decode(encoding_b64)
                        encoding_array = np.frombuffer(encoding_bytes, dtype=np.float64)
                        face_encodings.append(encoding_array)
                    
                    if face_encodings:
                        self.workers.append({
                            'id': worker_data.get('Id', 0),
                            'name': worker_data.get('Name', 'Unknown'),
                            'encodings': face_encodings
                        })
                        worker_count += 1
                        
                except Exception as e:
                    self.send_message("status", message=f"Error loading {filename}: {str(e)}")
            
            if worker_count > 0:
                self.recognition_enabled = True
                self.send_message("status", message=f"Loaded {worker_count} workers for recognition")
                return True
            else:
                self.send_message("status", message="No workers found with face data")
                return False
                
        except Exception as e:
            self.send_message("error", message=f"Failed to load workers: {str(e)}")
            return False
    
    def recognize_face(self, face_encoding):
        """Find BEST match across ALL workers with strict validation"""
        if not self.recognition_enabled or not self.workers:
            return None
            
        try:
            all_matches = []
            
            for worker in self.workers:
                best_distance = float('inf')
                best_encoding_index = -1
                
                for idx, stored_encoding in enumerate(worker['encodings']):
                    distance = face_recognition.face_distance([stored_encoding], face_encoding)[0]
                    if distance < best_distance:
                        best_distance = distance
                        best_encoding_index = idx
                
                confidence = round((1.0 - best_distance) * 100, 1)
                all_matches.append({
                    'worker': worker,
                    'distance': best_distance,
                    'confidence': confidence,
                    'encoding_index': best_encoding_index
                })
            
            all_matches.sort(key=lambda x: x['distance'])
            
            best_match = all_matches[0]
            second_best = all_matches[1] if len(all_matches) > 1 else None
            
            # Reject if not confident enough
            if best_match['distance'] > self.min_confidence_distance:
                self.send_message("status", 
                    message=f"Match too weak: {best_match['worker']['name']} at {best_match['confidence']}%")
                return None
            
            # Reject if ambiguous
            if second_best:
                distance_gap = second_best['distance'] - best_match['distance']
                if distance_gap < self.ambiguity_threshold:
                    self.send_message("status", 
                        message=f"Ambiguous: {best_match['worker']['name']} vs {second_best['worker']['name']}")
                    return None
            
            return {
                'worker_name': best_match['worker']['name'],
                'confidence': best_match['confidence'],
                'worker_id': best_match['worker']['id']
            }
            
        except Exception as e:
            self.send_message("error", message=f"Recognition error: {str(e)}")
            return None
    
    def initialize(self):
        """Initialize camera and face detection"""
        self.send_message("status", message="Initializing camera and loading workers...")
        
        self.load_workers()
        
        self.cap = cv2.VideoCapture(0)
        if not self.cap.isOpened():
            self.send_message("error", message="Could not open camera")
            return False
            
        self.face_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_frontalface_default.xml')
        
        status_msg = "Camera initialized"
        if self.recognition_enabled:
            status_msg += f" with {len(self.workers)} workers loaded"
        
        self.send_message("status", message=status_msg)
        return True
        
    def process_frame(self):
        """Process single frame with face detection"""
        ret, frame = self.cap.read()
        if not ret:
            return None
        
        frame = cv2.flip(frame, 1)
        
        # Performance optimization: only use slow detector when recognizing
        use_dlib_detector = self.recognition_enabled and (
            self.detection_mode == "detecting" or 
            self.detection_mode == "confirmation"
        )
        
        if use_dlib_detector:
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            face_locations = face_recognition.face_locations(rgb_frame)
            faces = [(left, top, right-left, bottom-top) for (top, right, bottom, left) in face_locations]
        else:
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            face_locations_cv = self.face_cascade.detectMultiScale(gray, 1.3, 5)
            faces = face_locations_cv
            face_locations = []
            rgb_frame = None
        
        if self.detection_mode == "detecting":
            if len(faces) > 0:
                self.detection_count += 1
                
                for (x, y, w, h) in faces:
                    cv2.rectangle(frame, (x, y), (x+w, y+h), (0, 255, 0), 3)
                    cv2.putText(frame, f"DETECTING {self.detection_count}/{self.detection_threshold}", 
                               (x, y-10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 2)
                
                cv2.putText(frame, f"DETECTION: {self.detection_count}/{self.detection_threshold}", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
                
                if self.detection_count >= self.detection_threshold:
                    recognition_result = None
                    if self.recognition_enabled and face_locations:
                        if rgb_frame is None:
                            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                        face_encodings = face_recognition.face_encodings(rgb_frame, face_locations)
                        if face_encodings:
                            recognition_result = self.recognize_face(face_encodings[0])
                    
                    if recognition_result:
                        self.send_message("signin_recognition", 
                                        worker_name=recognition_result['worker_name'],
                                        confidence=recognition_result['confidence'],
                                        worker_id=recognition_result['worker_id'])
                        
                        self.detection_mode = "confirmation"
                        self.recognition_result = recognition_result
                        self.send_message("status", message=f"Recognized: {recognition_result['worker_name']}")
                    else:
                        self.send_message("signin_unknown", message="Face not recognized")
                        self.detection_mode = "idle"
                        self.detection_count = 0
            else:
                if self.detection_count > 0:
                    self.detection_count -= 1
                    
                cv2.putText(frame, f"SCANNING: {self.detection_count}/{self.detection_threshold}", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 255, 0), 2)
        
        elif self.detection_mode == "confirmation":
            if len(faces) > 0 and self.recognition_result:
                for (x, y, w, h) in faces:
                    cv2.rectangle(frame, (x, y), (x+w, y+h), (255, 0, 255), 3)
                    label = f"CONFIRM: {self.recognition_result['worker_name']} ({self.recognition_result['confidence']:.0f}%)"
                    cv2.putText(frame, label, (x, y-10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 0, 255), 2)
              
                cv2.putText(frame, "WAITING FOR CONFIRMATION", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 0, 255), 2)
            else:
                cv2.putText(frame, "CONFIRMATION: No face visible", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 0, 255), 2)
        
        else:  # IDLE MODE - NO RECOGNITION!
            for (x, y, w, h) in faces:
                cv2.rectangle(frame, (x, y), (x+w, y+h), (0, 255, 255), 2)
                cv2.putText(frame, "FACE DETECTED", (x, y-10), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 255), 2)
            
            if len(faces) > 0:
                status = f"Ready - {len(faces)} face(s) visible"
                if self.recognition_enabled:
                    status += f" (Recognition: ON)"
                cv2.putText(frame, status, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 255, 255), 2)
            else:
                status = "Ready - No faces"
                if self.recognition_enabled:
                    status += f" ({len(self.workers)} workers loaded)"
                cv2.putText(frame, status, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 255, 255), 2)
        
        return frame
        
    def start_detection(self):
        """Start face detection mode for sign-in"""
        self.detection_mode = "detecting"
        self.detection_count = 0
        self.recognition_result = None
        self.send_message("status", message="Face detection started for sign-in")
        
    def stop_detection(self):
        """Stop detection mode"""
        self.detection_mode = "idle"
        self.detection_count = 0
        self.recognition_result = None
        self.send_message("status", message="Face detection stopped")
        
    def confirm_signin(self):
        """Handle confirmation of sign-in"""
        if self.recognition_result:
            self.send_message("signin_confirmed", 
                            worker_name=self.recognition_result['worker_name'],
                            confidence=self.recognition_result['confidence'],
                            worker_id=self.recognition_result['worker_id'])
            self.send_message("status", message=f"Sign-in confirmed for {self.recognition_result['worker_name']}")
        else:
            self.send_message("status", message="No recognition result to confirm")
        
        self.detection_mode = "idle"
        self.detection_count = 0
        self.recognition_result = None
    
    def reload_workers(self):
        """Reload worker data"""
        self.send_message("status", message="Reloading worker database...")
        self.load_workers()
        self.send_message("status", message=f"Worker database reloaded - {len(self.workers)} workers active")
        
    def stop(self):
        """Stop the video service"""
        self.running = False
        self.send_message("status", message="Stopping video service...")

    def run_with_commands(self):
        """Main video loop with stdin command checking"""
        if not self.initialize():
            return
            
        self.running = True
        self.send_message("status", message="Video feed started with face recognition")
        
        try:
            while self.running:
                # Check for commands from C# via stdin (non-blocking)
                check_commands()
                
                frame = self.process_frame()
                if frame is None:
                    break
                
                _, buffer = cv2.imencode('.jpg', frame)
                frame_base64 = base64.b64encode(buffer).decode('utf-8')
                self.send_message("frame", data=frame_base64)
                
                time.sleep(0.033)  # ~30 FPS
                
        except KeyboardInterrupt:
            self.send_message("status", message="Interrupted by user")
        finally:
            if self.cap:
                self.cap.release()
            self.send_message("status", message="Video service stopped")

# Global service instance
service = VideoDetectionService()

def check_commands():
    """Check stdin for commands from C# (non-blocking using select)"""
    # Use select to check if data is available without blocking
    if select.select([sys.stdin], [], [], 0)[0]:
        line = sys.stdin.readline().strip()
        if line:
            try:
                # Try parsing as JSON command first
                cmd = json.loads(line)
                handle_command(cmd)
            except json.JSONDecodeError:
                # Fallback to simple string command
                handle_simple_command(line)

def handle_command(cmd):
    """Handle JSON commands from C#"""
    global service
    cmd_type = cmd.get('type')
    
    if cmd_type == 'start_detection':
        service.start_detection()
    elif cmd_type == 'stop_detection':
        service.stop_detection()
    elif cmd_type == 'confirm_signin':
        service.confirm_signin()
    elif cmd_type == 'reload_workers':
        service.reload_workers()
    elif cmd_type == 'stop':
        service.stop()

def handle_simple_command(command):
    """Handle simple string commands from C#"""
    global service
    
    if command == "start_detection":
        service.start_detection()
    elif command == "stop_detection":
        service.stop_detection()
    elif command == "confirm_signin":
        service.confirm_signin()
    elif command == "reload_workers":
        service.reload_workers()
    elif command == "stop":
        service.stop()

if __name__ == "__main__":
    service.run_with_commands()