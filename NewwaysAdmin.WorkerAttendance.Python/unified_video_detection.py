# File: unified_video_detection.py
# Purpose: Unified video feed with face detection and recognition
# FINAL VERSION: Incorporates all fixes with improved command handling

import cv2
import base64
import json
import sys
import time
import os
import numpy as np
import face_recognition

class VideoDetectionService:
    def __init__(self):
        self.cap = None
        self.face_cascade = None
        self.running = False
        self.detection_mode = "idle"  # States: "idle", "detecting", "confirmation"
        self.detection_count = 0
        self.detection_threshold = 5
        self.recognition_result = None  # Store current recognition result
        
        # Face recognition data
        self.workers = []
        self.recognition_enabled = False
        self.recognition_tolerance = 0.6  # Maximum distance (lower is better)
        self.min_confidence_for_signin = 65.0  # Minimum confidence % for sign-in
        self.min_confidence_for_preview = 60.0  # Minimum confidence % for preview display
        
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
                        
                    # Skip inactive workers
                    if not worker_data.get('IsActive', True):
                        continue
                        
                    # Decode face encodings from base64
                    face_encodings = []
                    for encoding_b64 in worker_data.get('FaceEncodings', []):
                        encoding_bytes = base64.b64decode(encoding_b64)
                        encoding_array = np.frombuffer(encoding_bytes, dtype=np.float64)
                        face_encodings.append(encoding_array)
                    
                    if face_encodings:  # Only add workers with face data
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
        """Try to recognize a face against stored worker encodings - returns BEST match"""
        if not self.recognition_enabled or not self.workers:
            return None
            
        try:
            best_match = None
            best_distance = float('inf')
            
            # Check ALL workers and find the BEST match
            for worker in self.workers:
                for stored_encoding in worker['encodings']:
                    distance = face_recognition.face_distance([stored_encoding], face_encoding)[0]
                    
                    # Keep track of the best (lowest distance) match
                    if distance < best_distance and distance <= self.recognition_tolerance:
                        best_distance = distance
                        confidence = (1.0 - distance) * 100
                        best_match = {
                            'worker_name': worker['name'],
                            'confidence': round(confidence, 1),
                            'worker_id': str(worker['id']),
                            'distance': distance
                        }
            
            # Return the best match found (or None if no match within tolerance)
            if best_match:
                # Remove distance from return (internal use only)
                del best_match['distance']
                return best_match
            
            return None
            
        except Exception as e:
            self.send_message("error", message=f"Recognition error: {str(e)}")
            return None
    
    def initialize(self):
        """Initialize camera and face detection"""
        self.send_message("status", message="Initializing camera and loading workers...")
        
        # Load workers first
        self.load_workers()
        
        # Initialize camera
        self.cap = cv2.VideoCapture(0)
        if not self.cap.isOpened():
            self.send_message("error", message="Could not open camera")
            return False
        
        # Set camera properties
        self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
        self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
        self.cap.set(cv2.CAP_PROP_FPS, 30)
            
        # Load face cascade
        self.face_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_frontalface_default.xml')
        
        status_msg = "Camera initialized"
        if self.recognition_enabled:
            status_msg += f" with {len(self.workers)} workers loaded"
        
        self.send_message("status", message=status_msg)
        return True
        
    def process_frame(self):
        """Process single frame with face detection AND recognition"""
        ret, frame = self.cap.read()
        if not ret:
            return None
        
        # Flip frame for mirror effect
        frame = cv2.flip(frame, 1)
        
        # Detect faces
        if self.recognition_enabled:
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            face_locations = face_recognition.face_locations(rgb_frame)
            # Convert to (x, y, w, h) format
            faces = [(left, top, right-left, bottom-top) for (top, right, bottom, left) in face_locations]
        else:
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            face_locations_cv = self.face_cascade.detectMultiScale(gray, 1.3, 5)
            faces = face_locations_cv
            face_locations = []
        
        # Handle detection mode for sign-in
        if self.detection_mode == "detecting":
            if len(faces) > 0:
                self.detection_count += 1
                
                # Draw GREEN rectangles for detection
                for (x, y, w, h) in faces:
                    cv2.rectangle(frame, (x, y), (x+w, y+h), (0, 255, 0), 3)
                    cv2.putText(frame, f"DETECTING {self.detection_count}/{self.detection_threshold}", 
                               (x, y-10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 2)
                
                cv2.putText(frame, f"DETECTION: {self.detection_count}/{self.detection_threshold}", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
                
                # Check if threshold reached
                if self.detection_count >= self.detection_threshold:
                    recognition_result = None
                    
                    # Try recognition
                    if self.recognition_enabled and face_locations:
                        face_encodings = face_recognition.face_encodings(rgb_frame, face_locations)
                        if face_encodings:
                            recognition_result = self.recognize_face(face_encodings[0])
                    
                    # Only accept HIGH CONFIDENCE matches for sign-in
                    if recognition_result and recognition_result['confidence'] >= self.min_confidence_for_signin:
                        # Worker recognized with high confidence!
                        self.send_message("signin_recognition", 
                                        worker_name=recognition_result['worker_name'],
                                        confidence=recognition_result['confidence'],
                                        worker_id=recognition_result['worker_id'])
                        
                        # Move to confirmation state
                        self.detection_mode = "confirmation"
                        self.recognition_result = recognition_result
                        self.send_message("status", message=f"Recognized: {recognition_result['worker_name']}")
                    else:
                        # Unknown person or low confidence
                        if recognition_result:
                            self.send_message("status", message=f"Low confidence match rejected: {recognition_result['confidence']:.1f}%")
                        self.send_message("signin_unknown", message="Unknown person detected")
                        
                        # Reset to idle
                        self.detection_mode = "idle"
                        self.detection_count = 0
            else:
                # No face - decrement counter
                if self.detection_count > 0:
                    self.detection_count -= 1
                    
                cv2.putText(frame, f"SCANNING: {self.detection_count}/{self.detection_threshold}", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 255, 0), 2)
        
        elif self.detection_mode == "confirmation":
            # Waiting for confirmation
            if len(faces) > 0 and self.recognition_result:
                for (x, y, w, h) in faces:
                    cv2.rectangle(frame, (x, y), (x+w, y+h), (255, 0, 255), 3)  # Purple
                    label = f"CONFIRM: {self.recognition_result['worker_name']} ({self.recognition_result['confidence']:.0f}%)"
                    cv2.putText(frame, label, (x, y-10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 0, 255), 2)
                
                cv2.putText(frame, "WAITING FOR CONFIRMATION", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 0, 255), 2)
            else:
                cv2.putText(frame, "CONFIRMATION: No face visible", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 0, 255), 2)
        
        else:  # idle mode - real-time preview
            # Just show face detection - NO recognition in idle mode
            # Recognition only happens when user presses SIGN IN
            for (x, y, w, h) in faces:
                cv2.rectangle(frame, (x, y), (x+w, y+h), (0, 255, 255), 2)
                cv2.putText(frame, "FACE", (x, y-10), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 255), 2)
            
            # Status display
            if len(faces) > 0:
                status = f"Ready - {len(faces)} face(s) detected"
                cv2.putText(frame, status, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 255, 255), 2)
            else:
                status = "Ready - Press SIGN IN to start"
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
        """Handle confirmation of sign-in from C#"""
        if self.recognition_result:
            self.send_message("signin_confirmed", 
                            worker_name=self.recognition_result['worker_name'],
                            confidence=self.recognition_result['confidence'],
                            worker_id=self.recognition_result['worker_id'])
            self.send_message("status", message=f"Sign-in confirmed for {self.recognition_result['worker_name']}")
        else:
            self.send_message("status", message="No recognition result to confirm")
        
        # Reset to idle
        self.detection_mode = "idle"
        self.detection_count = 0
        self.recognition_result = None
    
    def reload_workers(self):
        """Reload worker data (for when new workers are added or deleted)"""
        self.send_message("status", message="Reloading worker database...")
        self.load_workers()
        self.send_message("status", message=f"Worker database reloaded - {len(self.workers)} workers active")
        
    def stop(self):
        """Stop the video service"""
        self.running = False
        self.send_message("status", message="Stopping video service...")

    def run_with_commands(self):
        """Main video loop with command checking"""
        if not self.initialize():
            return
            
        self.running = True
        self.send_message("status", message="Video feed started with face recognition")
        
        try:
            while self.running:
                # Check for commands from C#
                check_commands()
                
                # Process frame
                frame = self.process_frame()
                if frame is None:
                    break
                
                # Encode and send frame
                _, buffer = cv2.imencode('.jpg', frame)
                frame_base64 = base64.b64encode(buffer).decode('utf-8')
                self.send_message("frame", data=frame_base64)
                
                time.sleep(0.1)  # 10 FPS
                
        except Exception as e:
            self.send_message("error", message=f"Video error: {str(e)}")
        finally:
            if self.cap:
                self.cap.release()
            self.send_message("status", message="Video feed stopped")

# Global service instance
video_service = VideoDetectionService()

def check_commands():
    """Check for command files from C#"""
    import tempfile
    
    command_file = os.path.join(tempfile.gettempdir(), "face_detection_command.txt")
    
    try:
        if os.path.exists(command_file):
            with open(command_file, 'r') as f:
                command = f.read().strip()
            
            # Remove command file
            os.remove(command_file)
            
            video_service.send_message("status", message=f"Received command: {command}")
            
            # Process command
            if command == "start_detection":
                video_service.start_detection()
            elif command == "stop_detection":
                video_service.stop_detection()
            elif command == "confirm_signin":
                video_service.confirm_signin()
            elif command == "reload_workers":
                video_service.reload_workers()
            elif command == "stop":
                video_service.stop()
            else:
                video_service.send_message("status", message=f"Unknown command: {command}")
                
    except Exception as e:
        video_service.send_message("error", message=f"Command check error: {str(e)}")

if __name__ == "__main__":
    video_service.run_with_commands()