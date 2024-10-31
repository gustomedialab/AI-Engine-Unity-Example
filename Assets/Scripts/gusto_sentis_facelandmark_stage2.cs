using UnityEngine;
using UnityEngine.UI;
using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Gusto;
using Unity.Sentis;
using UnityEngine.UIElements;
using TMPro;


public class gusto_sentis_facelandmark_stage2 : MonoBehaviour
{    
    // public ModelAsset modelAsset;
    Model runtimeModel;
    List<Model.Output> output;
    float[,] anchors;
    Worker worker;
    Tensor<float> inputTensor;
    float measure_time;
    float max_det_time;
    float min_det_time = 1000.0f;
    float total_det_time;
    int frame_count = 1;

    bool has_det = false;
    WebCamTexture m_webCamTexture;
    RenderTexture m_tempRenderTexture;
    WebCamDevice[] m_devices;
    int camera_id = 0;
    
    [SerializeField] RectTransform m_debugRect;
    [SerializeField] RawImage m_rawImage;

    void OnGUI ()
    {
        GUI.Label(new Rect(15, 125, 450, 100), "Running Platform: " + Application.platform);
        GUI.Label(new Rect(15, 150, 450, 100), "Time Estimation(ms): " + measure_time);
        GUI.Label(new Rect(15, 175, 450, 100), "Avg / Min / Max: " + total_det_time / frame_count + " / " + min_det_time + " / " + max_det_time);
    }

    void Awake()
    {
        Application.targetFrameRate = 60;
    }

    void Start()
    {
        Debug.Log("Running Platform: " + Application.platform);
        m_devices = WebCamTexture.devices;

        if (m_devices.Length == 0)
        {
            throw new Exception("No camera device found");
        }

        int max_id = m_devices.Length - 1;
        if (camera_id > max_id)
        {
            if (m_devices.Length == 1)
            {
                throw new Exception("Camera with id " + camera_id + " not found. camera_id value should be 0");
            }
            else
            {
                throw new Exception("Camera with id " + camera_id +
                                    " not found. camera_id value should be between 0 and " + max_id.ToString());
            }
        }

        m_webCamTexture = new WebCamTexture();

        m_webCamTexture.Play(); //Start capturing image using webcam
        inputTensor = new Tensor<float>(new TensorShape(1, 256, 256, 3));
        // Debug.Log($"modelAssets: {(modelAsset != null ? modelAsset.name : "<NULL>")}");

        runtimeModel = ModelLoader.Load(Gusto.Utility.retrieve_streamingassets_data("Weights/face_landmarks_detector.sentis"));

        output = runtimeModel.outputs;

        for (int i = 0; i < output.Count; i++)
        {
            Debug.Log("output: " + output[i].name);
        }
        Debug.Log("output: " + output);
        worker = new Worker(runtimeModel, BackendType.CPU);
    }
    bool inferencePending = false;
    List<Tensor<float>> outputTensors = new List<Tensor<float>>();
    float start_time = 0.0f;
    float end_time = 0.0f;

    void Update()
    {
        if (m_webCamTexture.isPlaying == false)
        {
            return;
        }

        var rect = m_rawImage.rectTransform.rect;
        m_rawImage.texture = m_webCamTexture; //display the image on the RawImage


        TextureTransform face_landmark_transform = new TextureTransform();
        face_landmark_transform.SetTensorLayout(TensorLayout.NHWC);

        m_tempRenderTexture = RenderTexture.GetTemporary(m_webCamTexture.width, m_webCamTexture.height, 0, RenderTextureFormat.ARGB32);

        Graphics.Blit(m_webCamTexture, m_tempRenderTexture);


        // RenderTexture.ReleaseTemporary(m_tempRenderTexture);

        // Debug.Log("m_webCamTexture: " + m_webCamTexture.width + " " + m_webCamTexture.height);
        if (!inferencePending)
        {
            TextureConverter.ToTensor(m_tempRenderTexture, inputTensor, face_landmark_transform);

            start_time = Time.realtimeSinceStartup;
            worker.Schedule(inputTensor);

            for (int i = 0; i < output.Count; i++)
            {
                var outputTensor = worker.PeekOutput(output[i].name) as Tensor<float>;
                // outputTensor.ReadbackRequest();
                // outputTensor.ReadbackAndClone(); // not blocking
                outputTensor.ReadbackRequest();
                outputTensors.Add(outputTensor);
                // var cpuCopyTensor = await outputTensor.ReadbackAndCloneAsync();
            }
            inferencePending = true;
        }

        if (inferencePending) 
        {
            bool NotReady = false;
            // Debug.Log(outputTensors.Count);
            float[] dets = new float[10000];
            int[] dets_shape = new int[4];
            float[] scores = new float[1];
            int[] scores_shape = new int[2];
            /*
                outputname: Identity
                name: face points: (1, 1, 1, 1434)
                outputname: Identity_1
                name: tougue out of mouth: (1, 1, 1, 1)
                outputname: Identity_2
                name: score: (1, 1)
            */
            for (int i = 0; i < outputTensors.Count; i++)
            {
                if (outputTensors[i].IsReadbackRequestDone()){
                    if (output[i].name == "Identity"){
                        dets = outputTensors[i].DownloadToArray();
                        dets_shape = outputTensors[i].shape.ToArray();
                    }else if (output[i].name == "Identity_2"){
                        scores = outputTensors[i].DownloadToArray();
                        scores_shape = outputTensors[i].shape.ToArray();
                    }
                }else{
                    NotReady = true;
                }
            }
            if (!NotReady)
            {
                Debug.Log("dets_shape: " + dets_shape[0] + " " + dets_shape[1] + " " + dets_shape[2] + " " + dets_shape[3]);
                Debug.Log("scores_shape: " + scores_shape[0] + " " + scores_shape[1]);
                Debug.Log("scores: " + scores[0]);
                float[, ] face_points = new float[dets_shape[0] * dets_shape[1] * dets_shape[2] * dets_shape[3], 3];
                for (int i = 0; i < dets_shape[0] * dets_shape[1] * dets_shape[2] * dets_shape[3] / 3; i++)
                {
                    face_points[i, 0] = dets[i * 3];
                    face_points[i, 1] = dets[i * 3 + 1];
                    face_points[i, 2] = dets[i * 3 + 2];
                }
                inferencePending = false;
                outputTensors.Clear(); 

                end_time = Time.realtimeSinceStartup;
                measure_time = (end_time - start_time) * 1000.0f;
                min_det_time = Math.Min(min_det_time, measure_time);
                max_det_time = Math.Max(max_det_time, measure_time);
                total_det_time += measure_time;
            }
        }
        frame_count += 1;

        

    }

    void OnDestroy()
    {
        worker.Dispose();
        inputTensor.Dispose();
    }
}
