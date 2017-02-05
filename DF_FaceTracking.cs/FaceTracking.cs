using PubNubMessaging.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace DF_FaceTracking.cs
{
    internal class FaceTracking
    {
        private readonly MainForm m_form;
        private FPSTimer m_timer;
        private bool m_wasConnected;

        //Queue containing depth image - for synchronization purposes
        byte[] LUT;
        private Queue<PXCMImage> m_images;
        private const int NumberOfFramesToDelay = 3;
        private int _framesCounter = 0;
        private float _maxRange;

        public FaceTracking(MainForm form)
        {
            m_form = form;
            m_images = new Queue<PXCMImage>();
            LUT = Enumerable.Repeat((byte)0, 256).ToArray();
            LUT[255] = 1;
        }

        #region gesture portion
        private void DisplayGesture(PXCMHandData handAnalysis, int frameNumber)
        {

            int firedGesturesNumber = handAnalysis.QueryFiredGesturesNumber();
            string gestureStatusLeft = string.Empty;
            string gestureStatusRight = string.Empty;
            if (firedGesturesNumber == 0)
            {

                return;
            }
            m_form.clearStatus();
            /*
            for (int i = 0; i < firedGesturesNumber; i++)
            {
                PXCMHandData.GestureData gestureData;
                if (handAnalysis.QueryFiredGestureData(i, out gestureData) == pxcmStatus.PXCM_STATUS_NO_ERROR)
                {
                    PXCMHandData.IHand handData;
                    if (handAnalysis.QueryHandDataById(gestureData.handId, out handData) != pxcmStatus.PXCM_STATUS_NO_ERROR)
                        return;

                    PXCMHandData.BodySideType bodySideType = handData.QueryBodySide();
                    if (bodySideType == PXCMHandData.BodySideType.BODY_SIDE_LEFT)
                    {
                        gestureStatusLeft += "Left Hand Gesture: " + gestureData.name;
                    }
                    else if (bodySideType == PXCMHandData.BodySideType.BODY_SIDE_RIGHT)
                    {
                        gestureStatusRight += "Right Hand Gesture: " + gestureData.name;
                    }

                }
            
            }
            if (gestureStatusLeft == String.Empty)
                form.UpdateInfo("Frame " + frameNumber + ") " + gestureStatusRight + "\n", Color.SeaGreen);
            else
                form.UpdateInfo("Frame " + frameNumber + ") " + gestureStatusLeft + ", " + gestureStatusRight + "\n", Color.SeaGreen);
            */
        }

        /* Displaying current frames hand joints */
        private void DisplayJoints(PXCMHandData handOutput, long timeStamp = 0)
        {

                //Iterate hands
                PXCMHandData.JointData[][] nodes = new PXCMHandData.JointData[][] { new PXCMHandData.JointData[0x20], new PXCMHandData.JointData[0x20] };
                int numOfHands = handOutput.QueryNumberOfHands();
                for (int i = 0; i < numOfHands; i++)
                {
                    //Get hand by time of appearence
                    //PXCMHandAnalysis.HandData handData = new PXCMHandAnalysis.HandData();
                    PXCMHandData.IHand handData;
                    if (handOutput.QueryHandData(PXCMHandData.AccessOrderType.ACCESS_ORDER_BY_TIME, i, out handData) == pxcmStatus.PXCM_STATUS_NO_ERROR)
                    {
                        if (handData != null)
                        {
                            //Iterate Joints
                            for (int j = 0; j < 0x20; j++)
                            {
                                PXCMHandData.JointData jointData;
                                handData.QueryTrackedJoint((PXCMHandData.JointType)j, out jointData);
                                nodes[i][j] = jointData;




                            } // end iterating over joints
                        }
                    }
                } // end itrating over hands

                m_form.DisplayJoints(nodes, numOfHands);
            
        }


        #endregion

        private void DisplayDeviceConnection(bool isConnected)
        {
            if (isConnected && !m_wasConnected) m_form.UpdateStatus("Device Reconnected", MainForm.Label.StatusLabel);
            else if (!isConnected && m_wasConnected)
                m_form.UpdateStatus("Device Disconnected", MainForm.Label.StatusLabel);
            m_wasConnected = isConnected;
        }

        private void DisplayPicture(PXCMImage image)
        {
            PXCMImage.ImageData data;
            if (image.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out data) <
                pxcmStatus.PXCM_STATUS_NO_ERROR) return;
            m_form.DrawBitmap(data.ToBitmap(0, image.info.width, image.info.height));
            m_timer.Tick("");
            image.ReleaseAccess(data);
        }

        private void CheckForDepthStream(PXCMCapture.Device.StreamProfileSet profiles, PXCMFaceModule faceModule)
        {
            PXCMFaceConfiguration faceConfiguration = faceModule.CreateActiveConfiguration();
            if (faceConfiguration == null)
            {
                Debug.Assert(faceConfiguration != null);
                return;
            }

            PXCMFaceConfiguration.TrackingModeType trackingMode = faceConfiguration.GetTrackingMode();
            faceConfiguration.Dispose();

            if (trackingMode != PXCMFaceConfiguration.TrackingModeType.FACE_MODE_COLOR_PLUS_DEPTH) return;
            if (profiles.depth.imageInfo.format == PXCMImage.PixelFormat.PIXEL_FORMAT_DEPTH) return;
            PXCMCapture.DeviceInfo dinfo;
            m_form.Devices.TryGetValue(m_form.GetCheckedDevice(), out dinfo);

            if (dinfo != null)
                MessageBox.Show(
                    String.Format("Depth stream is not supported for device: {0}. \nUsing 2D tracking", dinfo.name),
                    @"Face Tracking",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void FaceAlertHandler(PXCMFaceData.AlertData alert)
        {
            m_form.UpdateStatus(alert.label.ToString(), MainForm.Label.StatusLabel);
        }

        public void SimplePipeline()
        {
            PXCMSenseManager pp = m_form.Session.CreateSenseManager();
            bool liveCamera = false;

            if (pp == null)
            {
                throw new Exception("PXCMSenseManager null");
            }

            // Set Resolution
            var selectedRes = m_form.GetCheckedColorResolution();

            if (selectedRes != null && !m_form.GetPlaybackState())  
            {
                // activate filter only live/record mode , no need in playback mode
                var set = new PXCMCapture.Device.StreamProfileSet
                {
                    color =
                    {
                        frameRate = selectedRes.Item2,
                        imageInfo =
                        {
                            format = selectedRes.Item1.format,
                            height = selectedRes.Item1.height,
                            width = selectedRes.Item1.width
                        }
                    }
                };
                pp.captureManager.FilterByStreamProfiles(set);
            }

            // Set Source & Landmark Profile Index 
            PXCMCapture.DeviceInfo info;                        
            if (m_form.GetPlaybackState())
            {
                //pp.captureManager.FilterByStreamProfiles(null);
                pp.captureManager.SetFileName(m_form.GetFileName(), false);
                pp.captureManager.SetRealtime(false);
            }
            else  if (m_form.GetRecordState())
                {
                    pp.captureManager.SetFileName(m_form.GetFileName(), true);
                }
            
            // Set Module            
            pp.EnableFace();
            PXCMFaceModule faceModule = pp.QueryFace();
            if (faceModule == null)
            {
                Debug.Assert(faceModule != null);
                return;
            }
            
            PXCMFaceConfiguration moduleConfiguration = faceModule.CreateActiveConfiguration();

            if (moduleConfiguration == null)
            {
                Debug.Assert(moduleConfiguration != null);
                return;
            }
            
            PXCMFaceConfiguration.TrackingModeType mode = m_form.GetCheckedProfile().Contains("3D")
                ? PXCMFaceConfiguration.TrackingModeType.FACE_MODE_COLOR_PLUS_DEPTH
                : PXCMFaceConfiguration.TrackingModeType.FACE_MODE_COLOR;

            moduleConfiguration.SetTrackingMode(mode);

            moduleConfiguration.strategy = PXCMFaceConfiguration.TrackingStrategyType.STRATEGY_RIGHT_TO_LEFT;

            moduleConfiguration.detection.maxTrackedFaces = 1;
            moduleConfiguration.landmarks.maxTrackedFaces = 1;
            moduleConfiguration.pose.maxTrackedFaces = 1;
            
            PXCMFaceConfiguration.ExpressionsConfiguration econfiguration = moduleConfiguration.QueryExpressions();
            if (econfiguration == null)
            {
                throw new Exception("ExpressionsConfiguration null");
            }
            econfiguration.properties.maxTrackedFaces = 1;

            econfiguration.EnableAllExpressions();
            moduleConfiguration.detection.isEnabled = true;
            moduleConfiguration.landmarks.isEnabled = true;
            moduleConfiguration.pose.isEnabled = true;
            econfiguration.Enable();
        

            moduleConfiguration.EnableAllAlerts();
            moduleConfiguration.SubscribeAlert(FaceAlertHandler);

            pxcmStatus applyChangesStatus = moduleConfiguration.ApplyChanges();
            pp.EnableHand("Hand Module");
            PXCMHandModule handAnalysis = pp.QueryHand();

            m_form.UpdateStatus("Init Started", MainForm.Label.StatusLabel);

            if (applyChangesStatus < pxcmStatus.PXCM_STATUS_NO_ERROR || pp.Init() < pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                m_form.UpdateStatus("Init Failed", MainForm.Label.StatusLabel);
            }
            else
            {
                using (PXCMFaceData moduleOutput = faceModule.CreateOutput())
                {
                    PXCMSenseManager.Handler handler = new PXCMSenseManager.Handler();
                    handler.onModuleProcessedFrame = new PXCMSenseManager.Handler.OnModuleProcessedFrameDelegate(OnNewFrame);

                    PXCMHandConfiguration handConfiguration = handAnalysis.CreateActiveConfiguration();
                    PXCMHandData handData = handAnalysis.CreateOutput();

                    if (handConfiguration == null)
                    {
                        m_form.UpdateStatus("Failed Create Configuration", MainForm.Label.StatusLabel);
                        return;
                    }
                    if (handData == null)
                    {
                        m_form.UpdateStatus("Failed Create Output", MainForm.Label.StatusLabel);
                        return;
                    }

                    Debug.Assert(moduleOutput != null);
                    PXCMCapture.Device.StreamProfileSet profiles;

                    PXCMCaptureManager cmanager = pp.QueryCaptureManager();
                    if (cmanager == null)
                    {
                        throw new Exception("capture manager null");
                    }
                    PXCMCapture.Device device  =  cmanager.QueryDevice();

                    if (device == null)
                    {
                        throw new Exception("device null");
                    }

                    if(handAnalysis != null)
                    {

                        PXCMCapture.DeviceInfo dinfo;

//                        PXCMCapture.Device device = instance.captureManager.device;
                        if (device != null)
                        {
                            device.QueryDeviceInfo(out dinfo);
                            _maxRange = device.QueryDepthSensorRange().max;

                        }


                        if (handConfiguration != null)
                        {
                            handConfiguration.EnableAllAlerts();
                            handConfiguration.EnableSegmentationImage(true);
                            handConfiguration.ApplyChanges();
                            handConfiguration.Update();
                        }
                    }
                    
                    device.QueryStreamProfileSet(PXCMCapture.StreamType.STREAM_TYPE_DEPTH, 0, out profiles);
                    CheckForDepthStream(profiles, faceModule);


                    ushort threshold = device.QueryDepthConfidenceThreshold();
                    int filter_option = device.QueryIVCAMFilterOption();
                    int range_tradeoff = device.QueryIVCAMMotionRangeTradeOff();

                    device.SetDepthConfidenceThreshold(1);
                    device.SetIVCAMFilterOption(6);
                    device.SetIVCAMMotionRangeTradeOff(21);

                    int frameCounter = 0;
                    int frameNumber = 0;
                    device.SetMirrorMode(PXCMCapture.Device.MirrorMode.MIRROR_MODE_HORIZONTAL);



                    m_form.UpdateStatus("Streaming", MainForm.Label.StatusLabel);
                    m_timer = new FPSTimer(m_form);

                    while (!m_form.Stopped)
                    {

                        string gestureName = "spreadfingers";
                        if (string.IsNullOrEmpty(gestureName) == false)
                        {
                            if (handConfiguration.IsGestureEnabled(gestureName) == false)
                            {
                                handConfiguration.DisableAllGestures();
                                handConfiguration.EnableGesture(gestureName, true);
                                handConfiguration.ApplyChanges();
                            }
                        }
                        else
                        {
                            handConfiguration.DisableAllGestures();
                            handConfiguration.ApplyChanges();
                        }


                        if (pp.AcquireFrame(true) < pxcmStatus.PXCM_STATUS_NO_ERROR)
                        {
                            break;
                        }

                        frameCounter++;

                        if (pp.AcquireFrame(true) < pxcmStatus.PXCM_STATUS_NO_ERROR) break;
                        var isConnected = pp.IsConnected();
                        DisplayDeviceConnection(isConnected);
                        if (isConnected)
                        {
                            var sample = pp.QueryFaceSample();
                            if (sample == null)
                            {
                                pp.ReleaseFrame();
                                continue;
                            }
                            DisplayPicture(sample.color);

                            moduleOutput.Update();
                            if (moduleConfiguration.QueryRecognition().properties.isEnabled)
                                UpdateRecognition(moduleOutput);

                            if (handData != null)
                            {
                                handData.Update();
                            }

                            PXCMCapture.Sample sample2 = pp.QueryHandSample();
                            if (sample2 != null && sample2.depth != null)
                            {
                                //                                DisplayPicture(sample.depth, handData);

                                if (handData != null)
                                {
                                    frameNumber = liveCamera ? frameCounter : pp.captureManager.QueryFrameIndex();

                                    DisplayJoints(handData);
                                    DisplayGesture(handData, frameNumber);
                                }
                            }


                            m_form.DrawGraphics(moduleOutput);
                            m_form.UpdatePanel();
                        }
                        pp.ReleaseFrame();
                    }
                    // Clean Up
                    if (handData != null) handData.Dispose();
                    if (handConfiguration != null) handConfiguration.Dispose();

                    device.SetDepthConfidenceThreshold(threshold);
                    device.SetIVCAMFilterOption(filter_option);
                    device.SetIVCAMMotionRangeTradeOff(range_tradeoff);
                }

                moduleConfiguration.UnsubscribeAlert(FaceAlertHandler);
                moduleConfiguration.ApplyChanges();
                m_form.UpdateStatus("Stopped", MainForm.Label.StatusLabel);
            }
            moduleConfiguration.Dispose();
            pp.Close();
            pp.Dispose();
        }

        public static pxcmStatus OnNewFrame(Int32 mid, PXCMBase module, PXCMCapture.Sample sample)
        {
            return pxcmStatus.PXCM_STATUS_NO_ERROR;
        }

        private void UpdateRecognition(PXCMFaceData faceOutput)
        {
            //TODO: add null checks
            if (m_form.Register) RegisterUser(faceOutput);
            if (m_form.Unregister) UnregisterUser(faceOutput);
        }

        private void RegisterUser(PXCMFaceData faceOutput)
        {
            m_form.Register = false;
            if (faceOutput.QueryNumberOfDetectedFaces() <= 0)
                return;

            PXCMFaceData.Face qface = faceOutput.QueryFaceByIndex(0);
            if (qface == null)
            {
                throw new Exception("PXCMFaceData.Face null");
            }
            PXCMFaceData.RecognitionData rdata = qface.QueryRecognition();
            if (rdata == null)
            {
                throw new Exception(" PXCMFaceData.RecognitionData null");
            }
            rdata.RegisterUser();
        }

        private void UnregisterUser(PXCMFaceData faceOutput)
        {
            m_form.Unregister = false;
            if (faceOutput.QueryNumberOfDetectedFaces() <= 0)
            {
                return;
            }

            var qface = faceOutput.QueryFaceByIndex(0);
            if (qface == null)
            {
                throw new Exception("PXCMFaceData.Face null");
            }

            PXCMFaceData.RecognitionData rdata = qface.QueryRecognition();
            if (rdata == null)
            {
                throw new Exception(" PXCMFaceData.RecognitionData null");
            }

            if (!rdata.IsRegistered())
            {
                return;
            }
            rdata.UnregisterUser();
        }
    }
}