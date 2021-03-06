﻿using System;
using System.Collections.Generic;
using GlmNet;
using SharpGL;
using SharpGL.Enumerations;
using SharpGL.Shaders;
using SharpGL.VertexBuffers;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using Microsoft.Win32;
using System.Windows;
using WooFractal.Objects;

namespace WooFractal
{
    /// <summary>
    /// A class that represents the scene for this sample.
    /// </summary>
    public class ShaderRenderer
    {
        uint[] _FrameBuffer = new uint[2];
        uint[] _RaytracerBuffer = new uint[2];
        uint[] _EffectFrameBuffer = new uint[2];
        uint[] _EffectRaytracerBuffer = new uint[2];
        uint[] _IntFrameBuffer = new uint[1];
        uint[] _PostprocessBuffer = new uint[1];
        uint[] _RandomNumbers = new uint[1];
        uint[] _RenderBuffer = new uint[1];
        uint[] _DepthFrameBuffer = new uint[1];
        uint[] _DepthCalcBuffer = new uint[1];
        PostProcess _PostProcess;
        mat4 _ViewMatrix;
        vec3 _Position;
        vec3 _SunDirection;
        Camera _Camera;
        FractalSettings _FractalSettings;

        int _TargetWidth;
        int _TargetHeight;
        int _ProgressiveSteps = 8;
        int _ProgressiveIndex = 0;
        int _MaxIterations = -1;

        public int GetTargetWidth() { return _TargetWidth; }
        public int GetTargetHeight() { return _TargetHeight; }

        public void SetShaderVars(mat4 viewMatrix, vec3 position, vec3 sunDirection, Camera camera, FractalSettings fractalSettings)
        {
            _ViewMatrix = viewMatrix;
            _Position = position;
            _SunDirection = sunDirection;
            _Camera = camera;
            _FramesRendered = 0;
            _ProgressiveIndex = 0;
            _FractalSettings = fractalSettings;
        }

        private void LoadRandomNumbers(OpenGL gl)
        {
            gl.GenTextures(1, _RandomNumbers);

            gl.ActiveTexture(OpenGL.GL_TEXTURE0);
            gl.BindTexture(OpenGL.GL_TEXTURE_2D, _RandomNumbers[0]);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MAG_FILTER, OpenGL.GL_LINEAR);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MIN_FILTER, OpenGL.GL_LINEAR);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_S, OpenGL.GL_CLAMP_TO_EDGE);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_T, OpenGL.GL_CLAMP_TO_EDGE);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_GENERATE_MIPMAP_SGIS, OpenGL.GL_FALSE); // automatic mipmap

            byte[] pixels = new byte[4 * 4 * 1024 * 1024]; // sizeof(int)

            var fs = new FileStream(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) + "\\WooFractal\\" + "randomSequences.vec2", FileMode.Open, FileAccess.Read);
            var len = (int)fs.Length;
            var bits = new byte[len];
            fs.Read(bits, 0, len);
            fs.Close();

            var floats = new float[len/2];
            int byteidx = 0;
            int idx = 0;
            
            for (int y = 0; y < 1024; y++)
            {
                for (int x = 0; x < 1024; x++)
                {
                    floats[idx++] = BitConverter.ToSingle(bits, byteidx);
                    byteidx+=4;
                    floats[idx++] = BitConverter.ToSingle(bits, byteidx);
                    byteidx += 4;
                    floats[idx++] = 0;
                    floats[idx++] = 1;
                }
            }

            Buffer.BlockCopy(floats, 0, pixels, 0, pixels.Length);

            gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA32F, 1024, 1024, 0,
                         OpenGL.GL_RGBA, OpenGL.GL_FLOAT, pixels);

            gl.BindTexture(OpenGL.GL_TEXTURE_2D, 0);
        }

        private void CheckForError(OpenGL gl)
        {
            ErrorCode errorCode = gl.GetErrorCode();
            if (errorCode != ErrorCode.NoError)
            {
                string errorDescription = gl.GetErrorDescription(gl.GetError());
                Debug.WriteLine(errorDescription);
            }
        }
        bool _Initialised = false;

        public void SetProgressive(bool progressive)
        {
            _ProgressiveSteps = progressive ? 8 : 1;
            _ProgressiveIndex = 0;
        }

        public void SetProgressive(bool progressive, int progressiveSteps)
        {
            _ProgressiveSteps = progressive ? progressiveSteps : 1;
            _ProgressiveIndex = 0;
        }

        public void SetMaxIterations(int maxIterations)
        {
            _MaxIterations = maxIterations;
        }

        public string GetShader(bool burnVariables)
        {
            string program = _Program;
            if (burnVariables)
            {
                ShaderVariables shaderVars = GetShaderVars();
                shaderVars.BurnVariables(ref program);
            }
            return program;
        }

        OpenGL _GL = null;
        bool _ShaderError = false;
        bool _BurnVariables = false;

        /// <summary>
        /// Initialises the Scene.
        /// </summary>
        /// <param name="gl">The OpenGL instance.</param>
        public void Initialise(OpenGL gl, int width, int height, mat4 viewMatrix, vec3 position, bool burnVariables)
        {
            Logger.Log("ShaderRenderer.Initialise Started");
            _GL = gl;
            if (_Initialised)
            {
                Destroy(gl);
                _Initialised = true;
            }

            _ViewMatrix = viewMatrix;
            _Position = position;
            _TargetWidth = width;
            _TargetHeight = height;
            _ProgressiveSteps = 1;
            _ProgressiveIndex = 0;
            _BurnVariables = burnVariables;

            //  We're going to specify the attribute locations for the position and normal, 
            //  so that we can force both shaders to explicitly have the same locations.
            const uint positionAttribute = 0;
            var attributeLocations = new Dictionary<uint, string>
            {
                {positionAttribute, "Position"}
            };

            Logger.Log("ShaderRenderer.Initialise Loading shaders from manifest");
            //  Create the raymarch shader
            shaderRayMarch = new ShaderProgram();
            if (_Program == null)
            {
                shaderRayMarch.Create(gl,
                    ManifestResourceLoader.LoadTextFile(@"Shaders\RayMarchProgressive.vert"),
                    ManifestResourceLoader.LoadTextFile(@"Shaders\RayMarch.frag"), attributeLocations);
            }
            else
            {
                if (_BurnVariables)
                {
                    ShaderVariables shaderVars = GetShaderVars();
                    shaderVars.BurnVariables(ref _Program);
                }
                _ShaderError = false;
                try
                {
                    shaderRayMarch.Create(gl,
                    ManifestResourceLoader.LoadTextFile(@"Shaders\RayMarchProgressive.vert"),
                    _Program, attributeLocations);
                }
                catch (ShaderCompilationException exception)
                {
                    _ShaderError = true;
                    MessageBox.Show(exception.Message +"\r\n" + exception.CompilerOutput);
                }
            }

            // Create the transfer shader
            string fragShader = @"
#version 130
in vec2 texCoord;
out vec4 FragColor;
uniform float mode; // 0=ramp, 1=exposure, 2=standard
uniform float toneFactor;
uniform float gammaFactor;
uniform float gammaContrast;
uniform sampler2D renderedTexture;

vec3 filmic(vec3 value)
{
float A=0.22;
float B=0.30;
float C=0.1;
float D=0.2;
float E=0.01;
float F=0.3;
return ((value*(A*value+C*B)+D*E)/(value*(A*value+B)+D*F)) - E/F;
}

void main()
{
 vec4 rgb = texture(renderedTexture, vec2((texCoord.x+1)*0.5, (texCoord.y+1)*0.5));
 FragColor=rgb;
// FragColor.rgb /= FragColor.a;
 
 // brightness/contrast
 float luminance = dot(FragColor.rgb, vec3(0.2126,0.7152,0.0722));
 float luminanceOut = gammaFactor * pow(luminance, gammaContrast);
 float multiplier = (max(0, luminance) * luminanceOut) / (luminance * luminance);
 FragColor.rgb *= multiplier;

if (mode>2.9 && mode<3.1)
{
 //filmic https://www.slideshare.net/ozlael/hable-john-uncharted2-hdr-lighting
 FragColor.rgb = filmic(FragColor.rgb)/filmic(vec3(toneFactor));
}
else if (mode>1.9 && mode<2.1)
{
 //reinhard https://imdoingitwrong.wordpress.com/2010/08/19/why-reinhard-desaturates-my-blacks-3/
 float nL = luminance * (1+luminance/(toneFactor*toneFactor)) / (1+luminance);
 FragColor.rgb *= nL;
}
else if (mode>0.9 && mode<1.1)
{
 //exposure originally Matt Fairclough
 FragColor.rgb = 1 - exp(-FragColor.rgb * toneFactor);
}
else
{
 FragColor.rgb /= toneFactor;
}
}
";
            shaderTransfer = new ShaderProgram();
            shaderTransfer.Create(gl,
                ManifestResourceLoader.LoadTextFile(@"Shaders\RayMarch.vert"),
                fragShader, attributeLocations);
            CheckForError(gl);

            fragShader = @"
#version 130
in vec2 texCoord;
out vec4 FragColor;
void main()
{
FragColor=vec4(0,0,0,0);
}
";
            shaderClean = new ShaderProgram();
            shaderClean.Create(gl,
                ManifestResourceLoader.LoadTextFile(@"Shaders\RayMarch.vert"),
                fragShader, attributeLocations);
            CheckForError(gl);

            // Create the transfer shader
            string fragShaderIntTransfer = @"
#version 130
in vec2 texCoord;
out vec4 FragColor;
uniform sampler2D renderedTexture;

void main()
{
 vec4 rgb = texture(renderedTexture, vec2((texCoord.x+1)*0.5, (texCoord.y+1)*0.5));
 FragColor=rgb;
}
";
            shaderIntTransfer = new ShaderProgram();
            shaderIntTransfer.Create(gl,
                ManifestResourceLoader.LoadTextFile(@"Shaders\RayMarch.vert"),
                fragShaderIntTransfer, attributeLocations);
            CheckForError(gl);

            Logger.Log("ShaderRenderer.Initialise Loading random numbers");
            LoadRandomNumbers(gl);
            Logger.Log("ShaderRenderer.Initialise Finished loading random numbers");

            float[] viewport = new float[4];
            gl.GetFloat(OpenGL.GL_VIEWPORT, viewport);
            
            gl.GenFramebuffersEXT(2, _FrameBuffer);
            CheckForError(gl);

            gl.GenTextures(2, _RaytracerBuffer);
            CheckForError(gl);
            for (int i = 0; i < 2; i++)
            {
                gl.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, _FrameBuffer[i]);

                gl.BindTexture(OpenGL.GL_TEXTURE_2D, _RaytracerBuffer[i]);
                gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MAG_FILTER, OpenGL.GL_LINEAR);
                gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MIN_FILTER, OpenGL.GL_LINEAR);
                gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_S, OpenGL.GL_CLAMP_TO_EDGE);
                gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_T, OpenGL.GL_CLAMP_TO_EDGE);
                gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_GENERATE_MIPMAP_SGIS, OpenGL.GL_FALSE); // automatic mipmap
//                gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA, (int)viewport[2], (int)viewport[3], 0,
  //                           OpenGL.GL_RGBA, OpenGL.GL_FLOAT, null);
                gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA32F, _TargetWidth, _TargetHeight, 0,
                             OpenGL.GL_RGBA, OpenGL.GL_FLOAT, null);
                CheckForError(gl);

                gl.FramebufferTexture2DEXT(OpenGL.GL_FRAMEBUFFER_EXT, OpenGL.GL_COLOR_ATTACHMENT0_EXT, OpenGL.GL_TEXTURE_2D, _RaytracerBuffer[i], 0);
                gl.FramebufferRenderbufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, OpenGL.GL_DEPTH_ATTACHMENT_EXT, OpenGL.GL_RENDERBUFFER_EXT, 0);
            }
            gl.BindTexture(OpenGL.GL_TEXTURE_2D, 0);

            gl.GenFramebuffersEXT(2, _EffectFrameBuffer);
            CheckForError(gl);

            gl.GenTextures(2, _EffectRaytracerBuffer);
            CheckForError(gl);
            for (int i = 0; i < 2; i++)
            {
                gl.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, _EffectFrameBuffer[i]);

                gl.BindTexture(OpenGL.GL_TEXTURE_2D, _EffectRaytracerBuffer[i]);
                gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MAG_FILTER, OpenGL.GL_LINEAR);
                gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MIN_FILTER, OpenGL.GL_LINEAR);
                gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_S, OpenGL.GL_CLAMP_TO_EDGE);
                gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_T, OpenGL.GL_CLAMP_TO_EDGE);
                gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_GENERATE_MIPMAP_SGIS, OpenGL.GL_FALSE); // automatic mipmap
                gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA32F, _TargetWidth, _TargetHeight, 0,
                             OpenGL.GL_RGBA, OpenGL.GL_FLOAT, null);
                CheckForError(gl);

                gl.FramebufferTexture2DEXT(OpenGL.GL_FRAMEBUFFER_EXT, OpenGL.GL_COLOR_ATTACHMENT0_EXT, OpenGL.GL_TEXTURE_2D, _EffectRaytracerBuffer[i], 0);
                gl.FramebufferRenderbufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, OpenGL.GL_DEPTH_ATTACHMENT_EXT, OpenGL.GL_RENDERBUFFER_EXT, 0);
            }
            gl.BindTexture(OpenGL.GL_TEXTURE_2D, 0);

            // and now initialise the integer framebuffer
            gl.GenFramebuffersEXT(1, _IntFrameBuffer);
            CheckForError(gl);
            gl.GenTextures(1, _PostprocessBuffer);
            CheckForError(gl);
            gl.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, _IntFrameBuffer[0]);
            gl.BindTexture(OpenGL.GL_TEXTURE_2D, _PostprocessBuffer[0]);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MAG_FILTER, OpenGL.GL_LINEAR);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MIN_FILTER, OpenGL.GL_LINEAR);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_S, OpenGL.GL_CLAMP_TO_EDGE);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_T, OpenGL.GL_CLAMP_TO_EDGE);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_GENERATE_MIPMAP_SGIS, OpenGL.GL_FALSE); // automatic mipmap
            gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA, _TargetWidth, _TargetHeight, 0, OpenGL.GL_RGBA, OpenGL.GL_UNSIGNED_BYTE, null);
            CheckForError(gl);
            gl.FramebufferTexture2DEXT(OpenGL.GL_FRAMEBUFFER_EXT, OpenGL.GL_COLOR_ATTACHMENT0_EXT, OpenGL.GL_TEXTURE_2D, _PostprocessBuffer[0], 0);
            gl.FramebufferRenderbufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, OpenGL.GL_DEPTH_ATTACHMENT_EXT, OpenGL.GL_RENDERBUFFER_EXT, 0);
            gl.BindTexture(OpenGL.GL_TEXTURE_2D, 0);

            _PostProcess = new PostProcess();
            _PostProcess.Initialise(gl);

            gl.GenFramebuffersEXT(1, _DepthFrameBuffer);
            gl.GenTextures(1, _DepthCalcBuffer);

            gl.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, _DepthFrameBuffer[0]);

            gl.BindTexture(OpenGL.GL_TEXTURE_2D, _DepthCalcBuffer[0]);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MAG_FILTER, OpenGL.GL_LINEAR);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_MIN_FILTER, OpenGL.GL_LINEAR_MIPMAP_LINEAR);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_S, OpenGL.GL_CLAMP_TO_EDGE);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_TEXTURE_WRAP_T, OpenGL.GL_CLAMP_TO_EDGE);
            gl.TexParameter(OpenGL.GL_TEXTURE_2D, OpenGL.GL_GENERATE_MIPMAP_SGIS, OpenGL.GL_TRUE);
            gl.TexImage2D(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA32F, 1, 1, 0, OpenGL.GL_RGBA, OpenGL.GL_FLOAT, null);
            gl.FramebufferTexture2DEXT(OpenGL.GL_FRAMEBUFFER_EXT, OpenGL.GL_COLOR_ATTACHMENT0_EXT, OpenGL.GL_TEXTURE_2D, _DepthCalcBuffer[0], 0);
            gl.FramebufferRenderbufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, OpenGL.GL_DEPTH_ATTACHMENT_EXT, OpenGL.GL_RENDERBUFFER_EXT, 0);

            _Initialised = true;
            
        /*    
            gl.GenRenderbuffersEXT(2, _RaytracerBuffer);
            gl.BindRenderbufferEXT(OpenGL.GL_RENDERBUFFER_EXT, _RaytracerBuffer[0]);
            gl.RenderbufferStorageEXT(OpenGL.GL_RENDERBUFFER_EXT, OpenGL.GL_RGBA32F, (int)viewport[2], (int)viewport[3]);
            gl.BindRenderbufferEXT(OpenGL.GL_RENDERBUFFER_EXT, _RaytracerBuffer[1]);
            gl.RenderbufferStorageEXT(OpenGL.GL_RENDERBUFFER_EXT, OpenGL.GL_RGBA32F, (int)viewport[2], (int)viewport[3]);
*/
       //     gl.GenRenderbuffersEXT(1, _RenderBuffer);
            //gl.BindRenderbufferEXT(OpenGL.GL_RENDERBUFFER_EXT, _RenderBuffer[0]);
            //gl.RenderbufferStorageEXT(OpenGL.GL_RENDERBUFFER_EXT, OpenGL.GL_RGBA, (int)viewport[2], (int)viewport[3]);
            Logger.Log("ShaderRenderer.Initialise Finished");
        }

        int _RaysPerPixel;
        double _FramesRendered, _Rays;
        Stopwatch _StopWatch;

        public double GetFrameCount()
        {
            return _FramesRendered;
        }

        public double GetRayCount()
        {
            return _Rays * _RaysPerPixel;
        }

        public double GetElapsedTime()
        {
            return (double)_StopWatch.ElapsedMilliseconds / 1000.0;
        }

        public void SetPostProcess(PostProcess postProcess)
        {
            _PostProcess = postProcess;
            if (_GL!=null)
                _PostProcess.Initialise(_GL);
        }

        public void Destroy(OpenGL gl)
        {
            gl.DeleteRenderbuffersEXT(2, _RaytracerBuffer);
            gl.DeleteFramebuffersEXT(2, _FrameBuffer);
            gl.DeleteRenderbuffersEXT(1, _DepthCalcBuffer);
            gl.DeleteFramebuffersEXT(1, _DepthFrameBuffer);
            shaderTransfer.Delete(gl);
            shaderClean.Delete(gl);
            shaderRayMarch.Delete(gl);
        }

        string _Program = null;

        public void Compile(OpenGL gl, string fragmentShader, int raysPerPixel)
        {
            _RaysPerPixel = raysPerPixel;
            _FramesRendered = 0;
            _Rays = 0;
            _StopWatch = new Stopwatch();
            _Program = fragmentShader;

            const uint positionAttribute = 0;
            var attributeLocations = new Dictionary<uint, string>
            {
                {positionAttribute, "Position"}
            };

            try
            {
                if (_BurnVariables)
                {
                    ShaderVariables shaderVars = GetShaderVars();
                    shaderVars.BurnVariables(ref fragmentShader);
                }

                DateTime start = DateTime.Now; 
                shaderRayMarch = new ShaderProgram();
                shaderRayMarch.Create(gl,
                    ManifestResourceLoader.LoadTextFile(@"Shaders\RayMarchProgressive.vert"),
                    fragmentShader, attributeLocations);
                DateTime end = DateTime.Now;
                TimeSpan duration = end - start;
                Console.WriteLine(duration.ToString());
            }
            catch (ShaderCompilationException exception)
            {
                _ShaderError = true;
                MessageBox.Show(exception.Message + "\r\n" + exception.CompilerOutput);
            }
        }

        private bool _PingPong = false;
        private float _FrameNumber = 0;

        public void CleanBuffers(OpenGL gl)
        {
            var shader = shaderClean;

            gl.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, _FrameBuffer[0]);
            shader.Bind(gl);
            gl.DrawArrays(OpenGL.GL_TRIANGLES, 0, 3);
            shader.Unbind(gl);
            
            gl.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, _FrameBuffer[1]);
            shader.Bind(gl);
            gl.DrawArrays(OpenGL.GL_TRIANGLES, 0, 3);
            shader.Unbind(gl);
            gl.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, 0);
            Debug.WriteLine("Cleaning buffers");
        }

        public void Clean(OpenGL gl)
        {
            CleanBuffers(gl);
        }

        public bool _Rendering = false;
        public void Start()
        {
            _Rendering = true;
            _StopWatch.Start();
        }

        public void Stop()
        {
            _Rendering = false;
            _StopWatch.Stop();
        }

        bool _SaveNextRender = false;

        ShaderVariables GetShaderVars()
        {
            ShaderVariables shaderVars = new ShaderVariables();

            shaderVars.Add("progressiveIndex", _ProgressiveIndex);
   //         shaderVars.Add("screenWidth", _TargetWidth);
    //        shaderVars.Add("screenHeight", _TargetHeight);
            shaderVars.Add("frameNumber", _FrameNumber++);
            shaderVars.Add("viewMatrix", _ViewMatrix);
            shaderVars.Add("camPos", _Position);
//            shaderVars.Add("renderedTexture", 0);
//            shaderVars.Add("randomNumbers", 1);
//            shaderVars.Add("depth", _Depth ? 1 : 0);
            shaderVars.Add("mouseX", _MouseX);
            shaderVars.Add("mouseY", _TargetHeight - _MouseY);
            shaderVars.Add("sunDirection", _SunDirection);
            shaderVars.Add("focusDepth", (float)_Camera._FocusDepth);
            shaderVars.Add("apertureSize", (float)_Camera._ApertureSize);
            shaderVars.Add("fov", (float)_Camera._FOV);
            shaderVars.Add("spherical", (float)_Camera._Spherical);
            shaderVars.Add("stereographic", (float)_Camera._Stereographic);
            shaderVars.Add("fogStrength", (float)_FractalSettings._RenderOptions._FogStrength);
            shaderVars.Add("fogSamples", (float)_FractalSettings._RenderOptions._FogSamples);
            shaderVars.Add("fogColour", _FractalSettings._RenderOptions._FogColour);
            shaderVars.Add("fogType", (_FractalSettings._RenderOptions._FogType == EFogType.Vanilla) ? 0.0f : 1.0f);
            shaderVars.Add("distanceExtents", (float)_FractalSettings._RenderOptions._DistanceExtents);

            _FractalSettings.SetFractalDeclerations(ref shaderVars);
            _FractalSettings.SetColourDeclerations(ref shaderVars);
            
            return shaderVars;
        }

        private void SceneRender(OpenGL gl)
        {
            ShaderVariables shaderVars = GetShaderVars();

            //  Get a reference to the raytracer shader.
            var shader = shaderRayMarch;
            shader.Bind(gl);

            shaderVars.Set(gl, shader);

            int rt1 = shader.GetUniformLocation(gl, "renderedTexture");
            int rn1 = shader.GetUniformLocation(gl, "randomNumbers");
            gl.Uniform1(rt1, 0);
            gl.Uniform1(rn1, 1);
            shader.SetUniform1(gl, "depth", _Depth ? 1 : 0);
            shader.SetUniform1(gl, "screenWidth", _TargetWidth);
            shader.SetUniform1(gl, "screenHeight", _TargetHeight);

            gl.ActiveTexture(OpenGL.GL_TEXTURE0);
            gl.BindTexture(OpenGL.GL_TEXTURE_2D, _RaytracerBuffer[_PingPong ? 0 : 1]);
            gl.ActiveTexture(OpenGL.GL_TEXTURE1);
            gl.BindTexture(OpenGL.GL_TEXTURE_2D, _RandomNumbers[0]);
            gl.ActiveTexture(OpenGL.GL_TEXTURE0);
            if (_Rendering)
            {
                gl.DrawArrays(OpenGL.GL_TRIANGLES, 0, (256 / _ProgressiveSteps) * 6);
    //            CheckForError(gl);
            }
            shader.Unbind(gl);
        }
        /// <summary>
        /// Renders the scene in retained mode.
        /// </summary>
        /// <param name="gl">The OpenGL instance.</param>
        /// <param name="useToonShader">if set to <c>true</c> use the toon shader, otherwise use a per-pixel shader.</param>
        public void Render(OpenGL gl)
        {
            if (_SaveNextRender)
            {
                SaveInternal(gl);
            }

            if (_MaxIterations > 0 && _FramesRendered > _MaxIterations - 1 && _Rendering==false)
                return;

            //  Clear the color and depth buffer.
            gl.ClearColor(0f, 0f, 0f, 0f);
            gl.Clear(OpenGL.GL_COLOR_BUFFER_BIT | OpenGL.GL_DEPTH_BUFFER_BIT | OpenGL.GL_STENCIL_BUFFER_BIT);

            int renderBuffer = 0;
            if (_PingPong) renderBuffer = 1;

            float[] viewport = new float[4];
            gl.GetFloat(OpenGL.GL_VIEWPORT, viewport);

            Debug.WriteLine("Rendering renderbuffer : " + renderBuffer);

            if (_Depth)
            {
                gl.Viewport(0, 0, 1, 1);
                gl.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, _DepthFrameBuffer[0]);

                SceneRender(gl);

                gl.Viewport(0, 0, (int)viewport[2], (int)viewport[3]);
                gl.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, 0);
                gl.BindTexture(OpenGL.GL_TEXTURE_2D, _DepthCalcBuffer[0]);
                int[] pixels = new int[4];
                gl.GetTexImage(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA, OpenGL.GL_FLOAT, pixels);
                float valr = BitConverter.ToSingle(BitConverter.GetBytes(pixels[0]), 0);
//                if (valr != valr)
  //                  throw new Exception("Depth test failed");
                //                float valg = BitConverter.ToSingle(BitConverter.GetBytes(pixels[1]), 0);
                //                float valb = BitConverter.ToSingle(BitConverter.GetBytes(pixels[2]), 0);
                //                float vala = BitConverter.ToSingle(BitConverter.GetBytes(pixels[3]), 0);
                _ImageDepth = valr;
                _ImageDepthSet = true;
                _Depth = false;
            }

            gl.Viewport(0, 0, _TargetWidth, _TargetHeight);
            gl.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, _FrameBuffer[renderBuffer]);

            SceneRender(gl);

            int target = 0;

            _PostProcess.Render(gl, _TargetWidth, _TargetHeight, ref target, _EffectFrameBuffer, _RaytracerBuffer[renderBuffer], _EffectRaytracerBuffer);

            // !!!!!!!!!!!!!!!! Tonemapping
            gl.Viewport(0, 0, _TargetWidth, _TargetHeight);
            gl.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, _IntFrameBuffer[0]);

            var shader = shaderTransfer;

            gl.ActiveTexture(OpenGL.GL_TEXTURE0);
            gl.BindTexture(OpenGL.GL_TEXTURE_2D, _EffectRaytracerBuffer[target]);

            shader.Bind(gl);
            shader.SetUniform1(gl, "renderedTexture", 0);
            shader.SetUniform1(gl, "gammaFactor", (float)_PostProcess._GammaFactor);
            shader.SetUniform1(gl, "gammaContrast", (float)_PostProcess._GammaContrast);
            shader.SetUniform1(gl, "mode", _PostProcess._ToneMappingMode);
            shader.SetUniform1(gl, "toneFactor", (float)_PostProcess._ToneFactor);

            gl.DrawArrays(OpenGL.GL_TRIANGLES, 0, 3);
//            CheckForError(gl);
            shader.Unbind(gl);

            // !!!!!!!!!!!!!!!!! Int to final framebuffer
            gl.Viewport(0, 0, (int)viewport[2], (int)viewport[3]);
            gl.BindFramebufferEXT(OpenGL.GL_FRAMEBUFFER_EXT, 0);

            // get a reference to the transfer shader
            shader = shaderIntTransfer;

            gl.ActiveTexture(OpenGL.GL_TEXTURE0);
            gl.BindTexture(OpenGL.GL_TEXTURE_2D, _PostprocessBuffer[0]);

            shader.Bind(gl);
            shader.SetUniform1(gl, "renderedTexture", 0);

            gl.DrawArrays(OpenGL.GL_TRIANGLES, 0, 3);


            gl.Viewport(0, 0, (int)viewport[2], (int)viewport[3]);
     //       CheckForError(gl);
            shader.Unbind(gl);

            // TIDY UP
            gl.BindTexture(OpenGL.GL_TEXTURE_2D, 0);
           
            if (_Rendering)
            {
                _FramesRendered += 1.0 / (double)_ProgressiveSteps;

                if (_MaxIterations > 0 && _FramesRendered > _MaxIterations-1)
                    _Rendering = false;
                else
                {
                    _ProgressiveIndex += 256 / _ProgressiveSteps;
                    _Rays = _TargetHeight * _TargetHeight * _FramesRendered;
                    if (_ProgressiveIndex >= 256)
                    {
                        _ProgressiveIndex = 0;
                        _PingPong = !_PingPong;
                    }
                }
            }
        }

        public void Save()
        {
            _SaveNextRender = true;
        }

        public void SaveInternal(OpenGL gl)
        {
            gl.BindTexture(OpenGL.GL_TEXTURE_2D, _PostprocessBuffer[0]);
            int[] pixels = new int[_TargetWidth * _TargetHeight];
            gl.GetTexImage(OpenGL.GL_TEXTURE_2D, 0, OpenGL.GL_RGBA, OpenGL.GL_UNSIGNED_BYTE, pixels);

            Bitmap bmp = new Bitmap(_TargetWidth, _TargetHeight);
            int x, y;
            for (y = 0; y < _TargetHeight; y++)
            {
                for (x = 0; x < _TargetWidth; x++)
                {
                    bmp.SetPixel(x, y, System.Drawing.Color.FromArgb((pixels[(x + ((_TargetHeight-1)-y) * _TargetWidth)]&0xFF),
                        (pixels[(x + ((_TargetHeight - 1) - y) * _TargetWidth)] & 0xFF00) >> 8,
                        (pixels[(x + ((_TargetHeight - 1) - y) * _TargetWidth)] & 0xFF0000) >> 16));
                }
            }

            string store = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) + "\\WooFractal\\Exports";
            if (!System.IO.Directory.Exists(store))
            {
                System.IO.Directory.CreateDirectory(store);
            }

            SaveFileDialog saveFileDialog1 = new SaveFileDialog();
            saveFileDialog1.InitialDirectory = store;
            saveFileDialog1.Filter = "PNG (*.png)|*.png";
            saveFileDialog1.FilterIndex = 1;

            if (saveFileDialog1.ShowDialog() == true)
            {
                bmp.Save(saveFileDialog1.FileName, System.Drawing.Imaging.ImageFormat.Png);
                bmp.Save(saveFileDialog1.FileName.Replace(".png", ".jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);
                ((MainWindow)System.Windows.Application.Current.MainWindow).SaveImageScene(saveFileDialog1.FileName.Replace(".png", ".wsd"));
            }

            _SaveNextRender = false;
        }

        bool _Depth;
        float _MouseX;
        float _MouseY;
        public float _ImageDepth;
        public bool _ImageDepthSet;

        public void GetDepth(float mousex, float mousey)
        {
            _Depth = true;
            _MouseX = mousex;
            _MouseY = mousey;
            _Rendering = true;
        }
        
        //  The shaders we use.
        private ShaderProgram shaderRayMarch;
        private ShaderProgram shaderTransfer;
        private ShaderProgram shaderClean;
        private ShaderProgram shaderIntTransfer;
    }
}
