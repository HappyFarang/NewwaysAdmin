# File: NewwaysAdmin.WorkerAttendance.Python/unified_video_detection.py
# Purpose: Unified video feed with face detection on command (FIXED with debug)

import cv2
import base64
import json
import sys
import time
import threading

class VideoDetectionService:
    def __init__(self):
        self.cap = None
        self.face_cascade = None
        self.running = False
        self.detection_mode = False
        self.detection_count = 0
        self.detection_threshold = 5  # Lower threshold - was 10
        
    def send_message(self, msg_type, **kwargs):
        """Send JSON message to C#"""
        message = {"type": msg_type, "timestamp": time.time(), **kwargs}
        print(json.dumps(message))
        sys.stdout.flush()
        
    def initialize(self):
        """Initialize camera and face detection"""
        self.send_message("status", message="Initializing camera...")
        
        self.cap = cv2.VideoCapture(0)
        if not self.cap.isOpened():
            self.send_message("error", message="Could not open camera")
            return False
            
        self.face_cascade = cv2.CascadeClassifier(cv2.data.haarcascades + 'haarcascade_frontalface_default.xml')
        self.send_message("status", message="Camera initialized")
        return True
        
    def process_frame(self):
        """Process single frame with face detection"""
        ret, frame = self.cap.read()
        if not ret:
            self.send_message("error", message="Failed to read frame")
            return None
            
        # Convert to grayscale for face detection
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        faces = self.face_cascade.detectMultiScale(gray, 1.3, 5)
        
        # Handle detection mode
        if self.detection_mode:
            if len(faces) > 0:
                self.detection_count += 1
                
                # Send debug info to C#
                self.send_message("status", message=f"Detection progress: {self.detection_count}/{self.detection_threshold}")
                
                # Draw GREEN rectangles for detection mode
                for (x, y, w, h) in faces:
                    cv2.rectangle(frame, (x, y), (x+w, y+h), (0, 255, 0), 3)
                    cv2.putText(frame, f"DETECTING {self.detection_count}/{self.detection_threshold}", (x, y-10), 
                               cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 2)
                
                cv2.putText(frame, f"DETECTION: {self.detection_count}/{self.detection_threshold}", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
                
                # Check if we have enough consistent detections
                if self.detection_count >= self.detection_threshold:
                    # Success! Send detection result
                    face_data = []
                    for i, (x, y, w, h) in enumerate(faces):
                        face_data.append({
                            "id": f"face_{i+1}",
                            "confidence": 0.8,
                            "position": {"x": int(x), "y": int(y), "width": int(w), "height": int(h)}
                        })
                    
                    self.send_message("detection_complete", 
                                    status="success", 
                                    message=f"Detected {len(faces)} face(s)",
                                    faces=face_data)
                    
                    # Reset detection mode
                    self.detection_mode = False
                    self.detection_count = 0
            else:
                # No faces in detection mode - but don't decrease as aggressively
                if self.detection_count > 0:
                    self.detection_count -= 1  # Only decrease by 1
                    self.send_message("status", message=f"Face lost, count now: {self.detection_count}")
                
                cv2.putText(frame, f"SCANNING: {self.detection_count}/{self.detection_threshold}", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 255, 0), 2)
        else:
            # Normal video mode - just show faces if detected
            for (x, y, w, h) in faces:
                cv2.rectangle(frame, (x, y), (x+w, y+h), (0, 255, 255), 2)  # Yellow for preview
                cv2.putText(frame, "FACE", (x, y-10), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 255), 2)
            
            if len(faces) > 0:
                cv2.putText(frame, f"Ready - {len(faces)} face(s) visible", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 255, 255), 2)
            else:
                cv2.putText(frame, "Ready - No faces", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 255, 255), 2)
        
        return frame
        
    def run_with_commands(self):
        """Main video loop with command checking"""
        if not self.initialize():
            return
            
        self.running = True
        self.send_message("status", message="Video feed started with command checking")
        
        try:
            while self.running:
                # Check for commands every frame
                check_commands()
                
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
    
    def run(self):
        """Main video loop"""
        if not self.initialize():
            return
            
        self.running = True
        self.send_message("status", message="Video feed started")
        
        try:
            while self.running:
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
            
    def start_detection(self):
        """Start face detection mode"""
        self.detection_mode = True
        self.detection_count = 0
        self.send_message("status", message="Face detection mode started")
        
    def stop_detection(self):
        """Stop detection mode"""
        self.detection_mode = False
        self.detection_count = 0
        self.send_message("status", message="Face detection mode stopped")
        
    def stop(self):
        """Stop the video service"""
        self.running = False

# Global service instance
video_service = VideoDetectionService()

def check_commands():
    """Check for command files from C#"""
    import tempfile
    import os
    
    command_file = os.path.join(tempfile.gettempdir(), "face_detection_command.txt")
    
    try:
        if os.path.exists(command_file):
            with open(command_file, 'r') as f:
                command = f.read().strip()
            
            # Delete the command file immediately
            os.remove(command_file)
            
            video_service.send_message("status", message=f"Received command: {command}")
            
            if command == "start_detection":
                video_service.send_message("status", message="Starting detection mode!")
                video_service.start_detection()
            elif command == "stop_detection":
                video_service.send_message("status", message="Stopping detection mode!")
                video_service.stop_detection()
            elif command == "stop":
                video_service.stop()
                
    except Exception as e:
        video_service.send_message("error", message=f"Command check error: {str(e)}")

if __name__ == "__main__":
    # Run main video loop with command checking
    video_service.run_with_commands()