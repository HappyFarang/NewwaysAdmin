# File: face_training_capture.py
# Purpose: Face training with unified JSON communication (same as unified_video_detection.py)

import cv2
import sys
import base64
import json
import time
import face_recognition
import numpy as np
import threading

class FaceTrainingCapture:
    def __init__(self):
        self.camera = None
        self.is_running = False
        
    def send_message(self, msg_type, **kwargs):
        """Send JSON message to C# - SAME FORMAT as unified_video_detection.py"""
        message = {"type": msg_type, "timestamp": time.time(), **kwargs}
        print(json.dumps(message))
        sys.stdout.flush()
        
    def start_training(self):
        """Initialize camera and start training session"""
        try:
            self.send_message("status", message="Initializing camera for face training...")
            
            # Initialize camera
            self.camera = cv2.VideoCapture(0)
            if not self.camera.isOpened():
                self.send_message("error", message="Cannot access camera")
                return False
                
            # Set camera properties for better quality
            self.camera.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
            self.camera.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
            self.camera.set(cv2.CAP_PROP_FPS, 30)
            
            self.is_running = True
            self.send_message("status", message="Face training camera ready")
            return True
            
        except Exception as e:
            self.send_message("error", message=f"Failed to initialize camera: {str(e)}")
            return False
    
    def capture_frame(self):
        """Capture and return current video frame"""
        if not self.camera:
            return None
            
        ret, frame = self.camera.read()
        if not ret:
            self.send_message("error", message="Failed to capture frame")
            return None
            
        # Flip frame horizontally for mirror effect
        frame = cv2.flip(frame, 1)
        
        # Draw face detection overlay
        frame_with_overlay = self.add_face_detection_overlay(frame)
        
        return frame_with_overlay
    
    def add_face_detection_overlay(self, frame):
        """Add face detection overlay to frame"""
        try:
            # Convert BGR to RGB for face_recognition
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            
            # Find face locations
            face_locations = face_recognition.face_locations(rgb_frame)
            
            # Draw rectangles around detected faces
            for (top, right, bottom, left) in face_locations:
                cv2.rectangle(frame, (left, top), (right, bottom), (0, 255, 0), 2)
                cv2.putText(frame, "Face Ready for Training", (left, top-10), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
            
            # Add training instructions overlay
            cv2.putText(frame, "TRAINING MODE - Click CAPTURE when ready", (10, 30), 
                       cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
            cv2.putText(frame, f"Faces detected: {len(face_locations)}", (10, 60), 
                       cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
            
            return frame
            
        except Exception as e:
            self.send_message("error", message=f"Error in face detection: {str(e)}")
            return frame
    
    def send_frame(self, frame):
        """Send video frame to C# as base64 JSON message"""
        try:
            _, buffer = cv2.imencode('.jpg', frame)
            frame_base64 = base64.b64encode(buffer).decode('utf-8')
            
            # Send frame using same format as unified_video_detection.py
            self.send_message("frame", data=frame_base64)
            
        except Exception as e:
            self.send_message("error", message=f"Error encoding frame: {str(e)}")
    
    def capture_face_encoding(self):
        """Capture face encoding from current frame"""
        try:
            if not self.camera:
                self.send_message("error", message="Camera not initialized")
                return None
                
            # Capture frame
            ret, frame = self.camera.read()
            if not ret:
                self.send_message("error", message="Failed to capture frame for encoding")
                return None
            
            # Convert to RGB for face_recognition
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            
            # Get face encodings
            face_locations = face_recognition.face_locations(rgb_frame)
            face_encodings = face_recognition.face_encodings(rgb_frame, face_locations)
            
            if not face_encodings:
                self.send_message("error", message="No face found for encoding")
                return None
            
            if len(face_encodings) > 1:
                self.send_message("status", message="Multiple faces detected, using first one")
            
            # Return the first face encoding
            encoding = face_encodings[0]
            self.send_message("status", message="Face encoding captured successfully")
            return encoding.tobytes()
            
        except Exception as e:
            self.send_message("error", message=f"Error capturing face encoding: {str(e)}")
            return None
    
    def read_commands(self):
        """Background thread to read commands from stdin"""
        try:
            for line in sys.stdin:
                if not self.is_running:
                    break
                command = line.strip()
                if command:
                    self.send_message("status", message=f"Received command: {command}")
                    self.process_command(command)
        except EOFError:
            # Normal when stdin is closed
            pass
        except Exception as e:
            self.send_message("error", message=f"Error reading commands: {str(e)}")
    
    def run_training_loop(self):
        """Main training loop with JSON communication"""
        if not self.start_training():
            return
        
        # Start command reading thread
        command_thread = threading.Thread(target=self.read_commands, daemon=True)
        command_thread.start()
        
        self.send_message("status", message="Training loop started - ready for commands")
            
        try:
            while self.is_running:
                # Capture and send video frame
                frame = self.capture_frame()
                if frame is not None:
                    self.send_frame(frame)
                
                # Small delay to prevent overwhelming the system
                time.sleep(0.1)  # 10 FPS
                
        except Exception as e:
            self.send_message("error", message=f"Training loop error: {str(e)}")
        finally:
            self.cleanup()
    
    def process_command(self, command):
        """Process commands from C# application"""
        if command == "CAPTURE":
            self.send_message("status", message="Processing capture request...")
            encoding = self.capture_face_encoding()
            if encoding:
                # Send encoding as base64 in JSON format
                encoded = base64.b64encode(encoding).decode('utf-8')
                self.send_message("encoding", data=encoded)
        elif command == "STOP":
            self.is_running = False
            self.send_message("status", message="Training session stopped")
        else:
            self.send_message("error", message=f"Unknown command: {command}")
    
    def cleanup(self):
        """Clean up resources"""
        if self.camera:
            self.camera.release()
        cv2.destroyAllWindows()
        self.send_message("status", message="Training session ended")

def main():
    """Main entry point"""
    try:
        trainer = FaceTrainingCapture()
        trainer.run_training_loop()
    except Exception as e:
        # Send error in JSON format
        error_msg = {"type": "error", "message": f"Fatal error: {str(e)}", "timestamp": time.time()}
        print(json.dumps(error_msg))
        sys.exit(1)

if __name__ == "__main__":
    main()