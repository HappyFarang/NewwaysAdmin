# File: NewwaysAdmin.WorkerAttendance.Python/face_training_capture.py
# Purpose: Python script for face training with live video feed and capture commands

import cv2
import sys
import base64
import io
import face_recognition
import numpy as np
from PIL import Image

class FaceTrainingCapture:
    def __init__(self):
        self.camera = None
        self.is_running = False
        
    def start_training(self):
        """Initialize camera and start training session"""
        try:
            # Initialize camera
            self.camera = cv2.VideoCapture(0)
            if not self.camera.isOpened():
                self.send_error("Cannot access camera")
                return False
                
            # Set camera properties for better quality
            self.camera.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
            self.camera.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
            self.camera.set(cv2.CAP_PROP_FPS, 30)
            
            self.is_running = True
            self.send_status("Camera initialized - training session ready")
            return True
            
        except Exception as e:
            self.send_error(f"Failed to initialize camera: {str(e)}")
            return False
    
    def capture_frame(self):
        """Capture and return current video frame"""
        if not self.camera:
            return None
            
        ret, frame = self.camera.read()
        if not ret:
            self.send_error("Failed to capture frame")
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
                cv2.putText(frame, "Face Detected", (left, top-10), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
            
            # Add training instructions overlay
            cv2.putText(frame, "Look at camera and click CAPTURE", (10, 30), 
                       cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
            cv2.putText(frame, f"Faces detected: {len(face_locations)}", (10, 60), 
                       cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 2)
            
            return frame
            
        except Exception as e:
            self.send_error(f"Face detection error: {str(e)}")
            return frame
    
    def capture_face_encoding(self):
        """Capture current frame and extract face encoding"""
        try:
            ret, frame = self.camera.read()
            if not ret:
                self.send_error("Failed to capture frame for encoding")
                return None
                
            # Flip frame horizontally for consistency
            frame = cv2.flip(frame, 1)
            
            # Convert BGR to RGB
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            
            # Find face locations and encodings
            face_locations = face_recognition.face_locations(rgb_frame)
            
            if len(face_locations) == 0:
                self.send_error("No face detected - please position your face in the camera")
                return None
            
            if len(face_locations) > 1:
                self.send_error("Multiple faces detected - ensure only one person is in frame")
                return None
            
            # Get face encoding
            face_encodings = face_recognition.face_encodings(rgb_frame, face_locations)
            
            if len(face_encodings) == 0:
                self.send_error("Failed to generate face encoding")
                return None
            
            self.send_status("Face encoding captured successfully!")
            return face_encodings[0].tobytes()
            
        except Exception as e:
            self.send_error(f"Face encoding error: {str(e)}")
            return None
    
    def frame_to_base64(self, frame):
        """Convert OpenCV frame to base64 string"""
        try:
            # Convert BGR to RGB
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            
            # Convert to PIL Image
            pil_image = Image.fromarray(rgb_frame)
            
            # Convert to base64
            buffer = io.BytesIO()
            pil_image.save(buffer, format='JPEG', quality=85)
            img_bytes = buffer.getvalue()
            
            return base64.b64encode(img_bytes).decode('utf-8')
            
        except Exception as e:
            self.send_error(f"Frame encoding error: {str(e)}")
            return None
    
    def send_frame(self, frame):
        """Send frame data to C# application"""
        base64_frame = self.frame_to_base64(frame)
        if base64_frame:
            print(f"FRAME:{base64_frame}", flush=True)
    
    def send_status(self, message):
        """Send status message to C# application"""
        print(f"STATUS:{message}", flush=True)
    
    def send_error(self, message):
        """Send error message to C# application"""
        print(f"ERROR:{message}", flush=True)
    
    def send_face_encoding(self, encoding_bytes):
        """Send face encoding to C# application"""
        if encoding_bytes:
            encoded = base64.b64encode(encoding_bytes).decode('utf-8')
            print(f"ENCODING:{encoded}", flush=True)
    
    def run_training_loop(self):
        """Main training loop"""
        if not self.start_training():
            return
            
        try:
            while self.is_running:
                # Capture and send video frame
                frame = self.capture_frame()
                if frame is not None:
                    self.send_frame(frame)
                
                # Check for commands from C#
                try:
                    # Non-blocking read attempt
                    import select
                    import sys
                    
                    if sys.stdin in select.select([sys.stdin], [], [], 0)[0]:
                        command = input().strip()
                        self.process_command(command)
                        
                except (EOFError, KeyboardInterrupt):
                    break
                except:
                    # select not available on Windows, use simpler approach
                    pass
                
                # Small delay to prevent overwhelming the system
                cv2.waitKey(30)
                
        except Exception as e:
            self.send_error(f"Training loop error: {str(e)}")
        finally:
            self.cleanup()
    
    def process_command(self, command):
        """Process commands from C# application"""
        if command == "CAPTURE":
            self.send_status("Processing capture request...")
            encoding = self.capture_face_encoding()
            if encoding:
                self.send_face_encoding(encoding)
        elif command == "STOP":
            self.is_running = False
            self.send_status("Training session stopped")
        else:
            self.send_error(f"Unknown command: {command}")
    
    def cleanup(self):
        """Clean up resources"""
        if self.camera:
            self.camera.release()
        cv2.destroyAllWindows()
        self.send_status("Training session ended")

def main():
    """Main entry point"""
    try:
        trainer = FaceTrainingCapture()
        trainer.run_training_loop()
    except Exception as e:
        print(f"ERROR:Fatal error: {str(e)}", flush=True)
        sys.exit(1)

if __name__ == "__main__":
    main()