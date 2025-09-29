# File: unified_video_detection.py
# Purpose: Face detection and recognition with improved best-match logic
# UPDATED: Fixed recognize_face to compare ALL workers before deciding

import cv2
import base64
import json
import sys
import time
import threading
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
        self.recognition_result = None  # Store current recognition result for confirmation
        
        # Face recognition data
        self.workers = []
        self.recognition_enabled = False
        
        # UPDATED: Strict thresholds to prevent false positives
        self.min_confidence_distance = 0.45  # Maximum distance (55% minimum confidence)
        self.ambiguity_threshold = 0.15      # Minimum gap between 1st and 2nd place (15%)
        
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
        """
        UPDATED: Find the BEST match across ALL workers with strict validation.
        
        This method now:
        1. Compares against ALL workers (not just first match)
        2. Uses the best encoding per worker
        3. Applies strict confidence threshold (55% minimum)
        4. Rejects ambiguous matches (top 2 must differ by 15%+)
        """
        if not self.recognition_enabled or not self.workers:
            return None
            
        try:
            # Step 1: Collect ALL matches with their distances
            all_matches = []
            
            for worker in self.workers:
                # Find BEST encoding match for THIS worker (not just first encoding)
                best_distance = float('inf')
                best_encoding_index = -1
                
                for idx, stored_encoding in enumerate(worker['encodings']):
                    distance = face_recognition.face_distance([stored_encoding], face_encoding)[0]
                    if distance < best_distance:
                        best_distance = distance
                        best_encoding_index = idx
                
                # Record this worker's best match
                confidence = round((1.0 - best_distance) * 100, 1)
                all_matches.append({
                    'worker': worker,
                    'distance': best_distance,
                    'confidence': confidence,
                    'encoding_index': best_encoding_index
                })
            
            # Step 2: Sort by distance (ascending = best matches first)
            all_matches.sort(key=lambda x: x['distance'])
            
            # Step 3: Get best and second-best matches
            best_match = all_matches[0]
            second_best = all_matches[1] if len(all_matches) > 1 else None
            
            # Step 4: Apply strict validation thresholds
            
            # Reject if best match is not confident enough (below 55% confidence)
            if best_match['distance'] > self.min_confidence_distance:
                self.send_message("status", 
                    message=f"Match too weak: Best was {best_match['worker']['name']} at {best_match['confidence']}% (minimum 55%)")
                return None
            
            # Reject if too close between top 2 matches (ambiguous result)
            if second_best:
                distance_gap = second_best['distance'] - best_match['distance']
                if distance_gap < self.ambiguity_threshold:
                    self.send_message("status", 
                        message=f"Ambiguous match: {best_match['worker']['name']} ({best_match['confidence']}%) vs {second_best['worker']['name']} ({second_best['confidence']}%) - gap only {distance_gap:.2f}")
                    return None
            
            # Step 5: Clear winner - return best match with extra info for logging
            self.send_message("status", 
                message=f"Clear match: {best_match['worker']['name']} at {best_match['confidence']}%")
            
            return {
                'worker_name': best_match['worker']['name'],
                'confidence': best_match['confidence'],
                'worker_id': best_match['worker']['id'],
                'distance': best_match['distance'],
                'encoding_used': best_match['encoding_index'],
                'second_best_name': second_best['worker']['name'] if second_best else None,
                'second_best_confidence': second_best['confidence'] if second_best else 0,
                'distance_gap': (second_best['distance'] - best_match['distance']) if second_best else 1.0
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
        """Process single frame with face detection AND recognition"""
        ret, frame = self.cap.read()
        if not ret:
            self.send_message("error", message="Failed to read frame")
            return None
        
        # PERFORMANCE FIX: Only use slow dlib detector when actually recognizing
        # In idle mode, use fast Haar Cascade for smooth frame rate
        use_dlib_detector = self.recognition_enabled and (
            self.detection_mode == "detecting" or 
            self.detection_mode == "confirmation"
        )
        
        if use_dlib_detector:
            # Slow but accurate dlib detector (for recognition)
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            face_locations = face_recognition.face_locations(rgb_frame)
            faces = [(left, top, right-left, bottom-top) for (top, right, bottom, left) in face_locations]
        else:
            # Fast Haar Cascade detector (for idle preview)
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            face_locations_cv = self.face_cascade.detectMultiScale(gray, 1.3, 5)
            faces = face_locations_cv
            face_locations = []
            rgb_frame = None  # Not needed in idle mode
        
        # Handle detection mode for sign-in
        if self.detection_mode == "detecting":
            if len(faces) > 0:
                self.detection_count += 1
                
                # Draw GREEN rectangles for detection
                for (x, y, w, h) in faces:
                    cv2.rectangle(frame, (x, y), (x+w, y+h), (0, 255, 0), 3)
                    cv2.putText(frame, f"DETECTING {self.detection_count}/{self.detection_threshold}", (x, y-10), 
                               cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 2)
                
                cv2.putText(frame, f"DETECTION: {self.detection_count}/{self.detection_threshold}", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
                
                # Check if threshold reached
                if self.detection_count >= self.detection_threshold:
                    recognition_result = None
                    if self.recognition_enabled and face_locations:
                        # Convert to RGB for recognition if not already done
                        if rgb_frame is None:
                            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                        face_encodings = face_recognition.face_encodings(rgb_frame, face_locations)
                        if face_encodings:
                            recognition_result = self.recognize_face(face_encodings[0])  # First face only
                    
                    if recognition_result:
                        self.send_message("signin_recognition", 
                                        worker_name=recognition_result['worker_name'],
                                        confidence=recognition_result['confidence'],
                                        worker_id=recognition_result['worker_id'])
                        
                        # Move to confirmation state, stop counting
                        self.detection_mode = "confirmation"
                        self.recognition_result = recognition_result
                        self.send_message("status", message=f"Waiting for confirmation: {recognition_result['worker_name']}")
                    else:
                        face_data = []
                        for i, (x, y, w, h) in enumerate(faces):
                            face_data.append({
                                "id": f"face_{i+1}",
                                "confidence": 0.8,
                                "position": {"x": int(x), "y": int(y), "width": int(w), "height": int(h)}
                            })
                        
                        self.send_message("signin_unknown", 
                                        message="Face not recognized - please try again",
                                        faces=face_data)
                        
                        # Reset to idle
                        self.detection_mode = "idle"
                        self.detection_count = 0
            else:
                if self.detection_count > 0:
                    self.detection_count -= 1
                    
                cv2.putText(frame, f"SCANNING: {self.detection_count}/{self.detection_threshold}", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 255, 0), 2)
        
        elif self.detection_mode == "confirmation":
            # Show confirmation UI, don't increment count
            if len(faces) > 0:
                for (x, y, w, h) in faces:
                    cv2.rectangle(frame, (x, y), (x+w, y+h), (255, 0, 255), 3)  # Purple for confirmation
                    cv2.putText(frame, f"CONFIRM: {self.recognition_result['worker_name']} ({self.recognition_result['confidence']:.0f}%)", 
                               (x, y-10), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 0, 255), 2)
              
                cv2.putText(frame, "WAITING FOR CONFIRMATION", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 0, 255), 2)
            else:
                cv2.putText(frame, "CONFIRMATION: No face visible", 
                           (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 1, (255, 0, 255), 2)
        
        else:  # idle mode (real-time preview - NO RECOGNITION for performance)
            # OPTIMIZATION: Skip recognition in idle mode to maintain good frame rate
            # Recognition only happens when user presses SIGN IN button (detecting mode)
            for (x, y, w, h) in faces:
                cv2.rectangle(frame, (x, y), (x+w, y+h), (0, 255, 255), 2)
                cv2.putText(frame, "FACE DETECTED", (x, y-10), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 255), 2)
            
            # Status display
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
        self.recognition_result = None  # Clear any prior result
        self.send_message("status", message="Face detection mode started for sign-in")
        
    def stop_detection(self):
        """Stop detection mode"""
        self.detection_mode = "idle"
        self.detection_count = 0
        self.recognition_result = None
        self.send_message("status", message="Face detection mode stopped")
        
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
        """Reload worker data (for when new workers are added)"""
        self.load_workers()
        
    def stop(self):
        """Stop the video service"""
        self.running = False

    def run_with_commands(self):
        """Main video loop with command checking"""
        if not self.initialize():
            return
            
        self.running = True
        self.send_message("status", message="Video feed started with face recognition")
        
        try:
            while self.running:
                check_commands()
                
                frame = self.process_frame()
                if frame is None:
                    break
                    
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
            
            os.remove(command_file)
            
            video_service.send_message("status", message=f"Received command: {command}")
            
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
                
    except Exception as e:
        video_service.send_message("error", message=f"Command check error: {str(e)}")

if __name__ == "__main__":
    video_service.run_with_commands()