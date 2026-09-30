using System.Reflection;
using OpenTK.Graphics.OpenGL4;

namespace ScreensaverStargate;

/// <summary>Per-frame values passed from the render loop to the renderer.</summary>
internal struct FrameParams
{
    public int Width, Height;
    public WallMode Mode;
    public float Scroll, ColorPhase;
    /// <summary>Seconds of simulated time, wrapped, for grain, gate weave and flicker.</summary>
    public float Time;
    public float Density, Thickness;
    public uint Seed;
    public float BloomStrength;
    public float LineHalfWidth;
    public bool LineSoft;
    public bool Diagnostics;

    public static FrameParams From(Settings s, int width, int height, uint seed)
    {
        float density = s.LineDensity / 100f;
        return new FrameParams
        {
            Width = width,
            Height = height,
            Mode = s.WallMode,
            Seed = seed,
            Density = s.WallMode == WallMode.Perspective ? 6f + 34f * density : 5f + 35f * density,
            Thickness = 0.08f + 0.77f * (s.LineThickness / 100f),
            BloomStrength = s.BloomIntensity / 100f * 1.6f,
            // Width is defined at 1080 pixels of height and scales with the real height.
            LineHalfWidth = Math.Max(0.5f, s.CenterLineWidth * height / 1080f * 0.5f),
            LineSoft = s.CenterLineEdge == CenterLineEdge.Soft,
            Diagnostics = s.ShowDiagnostics,
        };
    }
}

/// <summary>
/// Owns every GL resource of one context: shaders, the HDR scene target,
/// the bloom mip chain and an optional offscreen target for screenshots.
/// </summary>
internal sealed class SceneRenderer : IDisposable
{
    public const int TextCols = 44;
    public const int TextRows = 4;
    const int MaxBloomLevels = 6;
    const float BloomThreshold = 0.3f;
    const float BloomSpread = 0.6f;

    readonly int _vao;
    readonly int _sceneProg, _downProg, _upProg, _compProg;
    readonly int _fontTex;

    int _width, _height;
    int _sceneTex, _sceneFbo;
    int _levels;
    readonly int[] _downTex = new int[MaxBloomLevels], _downFbo = new int[MaxBloomLevels];
    readonly int[] _upTex = new int[MaxBloomLevels], _upFbo = new int[MaxBloomLevels];
    readonly int[] _levelW = new int[MaxBloomLevels], _levelH = new int[MaxBloomLevels];
    int _outTex, _outFbo, _outW, _outH;

    // Uniform locations.
    readonly int _sRes, _sMode, _sScroll, _sColor, _sDensity, _sThickness, _sSeed;
    readonly int _dSrc, _dHalf, _dPrefilter, _dThreshold;
    readonly int _uSrc, _uAdd, _uHalf, _uSpread;
    readonly int _cScene, _cBloom, _cFont, _cRes, _cBloomStrength, _cLineHalf, _cLineSoft, _cDiag, _cTextScale, _cText, _cTime, _cFilmScale;

    public SceneRenderer()
    {
        string vert = LoadShader("fullscreen_vert.glsl");
        _sceneProg = BuildProgram(vert, LoadShader("scene_frag.glsl"), "scene");
        _downProg = BuildProgram(vert, LoadShader("bloom_down_frag.glsl"), "bloom_down");
        _upProg = BuildProgram(vert, LoadShader("bloom_up_frag.glsl"), "bloom_up");
        _compProg = BuildProgram(vert, LoadShader("composite_frag.glsl"), "composite");

        _sRes = GL.GetUniformLocation(_sceneProg, "uRes");
        _sMode = GL.GetUniformLocation(_sceneProg, "uMode");
        _sScroll = GL.GetUniformLocation(_sceneProg, "uScroll");
        _sColor = GL.GetUniformLocation(_sceneProg, "uColorPhase");
        _sDensity = GL.GetUniformLocation(_sceneProg, "uDensity");
        _sThickness = GL.GetUniformLocation(_sceneProg, "uThickness");
        _sSeed = GL.GetUniformLocation(_sceneProg, "uSeed");

        _dSrc = GL.GetUniformLocation(_downProg, "uSrc");
        _dHalf = GL.GetUniformLocation(_downProg, "uHalfPixel");
        _dPrefilter = GL.GetUniformLocation(_downProg, "uPrefilter");
        _dThreshold = GL.GetUniformLocation(_downProg, "uThreshold");

        _uSrc = GL.GetUniformLocation(_upProg, "uSrc");
        _uAdd = GL.GetUniformLocation(_upProg, "uAdd");
        _uHalf = GL.GetUniformLocation(_upProg, "uHalfPixel");
        _uSpread = GL.GetUniformLocation(_upProg, "uSpread");

        _cScene = GL.GetUniformLocation(_compProg, "uScene");
        _cBloom = GL.GetUniformLocation(_compProg, "uBloom");
        _cFont = GL.GetUniformLocation(_compProg, "uFont");
        _cRes = GL.GetUniformLocation(_compProg, "uRes");
        _cBloomStrength = GL.GetUniformLocation(_compProg, "uBloomStrength");
        _cLineHalf = GL.GetUniformLocation(_compProg, "uLineHalfWidth");
        _cLineSoft = GL.GetUniformLocation(_compProg, "uLineSoft");
        _cDiag = GL.GetUniformLocation(_compProg, "uDiag");
        _cTextScale = GL.GetUniformLocation(_compProg, "uTextScale");
        _cText = GL.GetUniformLocation(_compProg, "uText");
        _cTime = GL.GetUniformLocation(_compProg, "uTime");
        _cFilmScale = GL.GetUniformLocation(_compProg, "uFilmScale");

        _vao = GL.GenVertexArray();

        byte[] atlas = GlyphFont.BuildAtlas(out int fw, out int fh);
        _fontTex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _fontTex);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8, fw, fh, 0, PixelFormat.Red, PixelType.UnsignedByte, atlas);
        SetSampling(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.FramebufferSrgb);
    }

    /// <summary>Uploads the diagnostics text (glyph indices, TextCols * TextRows).</summary>
    public void SetText(int[] glyphs)
    {
        GL.UseProgram(_compProg);
        GL.Uniform1(_cText, glyphs.Length, glyphs);
    }

    /// <summary>Renders one frame into the window, or into the offscreen target when requested.</summary>
    public void Render(in FrameParams p, bool offscreen)
    {
        if (p.Width < 1 || p.Height < 1)
            return;
        if (p.Width != _width || p.Height != _height)
            Resize(p.Width, p.Height);

        GL.BindVertexArray(_vao);

        // 1. Scene into the HDR target.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFbo);
        GL.Viewport(0, 0, _width, _height);
        GL.UseProgram(_sceneProg);
        GL.Uniform2(_sRes, (float)_width, (float)_height);
        GL.Uniform1(_sMode, p.Mode == WallMode.Perspective ? 0 : 1);
        GL.Uniform1(_sScroll, p.Scroll);
        GL.Uniform1(_sColor, p.ColorPhase);
        GL.Uniform1(_sDensity, p.Density);
        GL.Uniform1(_sThickness, p.Thickness);
        GL.Uniform1(_sSeed, p.Seed);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

        // 2. Bloom: downsample chain with bright pass, then upsample and accumulate.
        bool bloom = p.BloomStrength > 0f && _levels > 0;
        int bloomTex = _sceneTex;
        if (bloom)
        {
            GL.UseProgram(_downProg);
            GL.Uniform1(_dSrc, 0);
            GL.Uniform1(_dThreshold, BloomThreshold);
            GL.ActiveTexture(TextureUnit.Texture0);
            for (int i = 0; i < _levels; i++)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _downFbo[i]);
                GL.Viewport(0, 0, _levelW[i], _levelH[i]);
                GL.BindTexture(TextureTarget.Texture2D, i == 0 ? _sceneTex : _downTex[i - 1]);
                GL.Uniform2(_dHalf, 0.5f / _levelW[i], 0.5f / _levelH[i]);
                GL.Uniform1(_dPrefilter, i == 0 ? 1 : 0);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }

            bloomTex = _downTex[_levels - 1];
            if (_levels > 1)
            {
                GL.UseProgram(_upProg);
                GL.Uniform1(_uSrc, 0);
                GL.Uniform1(_uAdd, 1);
                GL.Uniform1(_uSpread, BloomSpread);
                for (int i = _levels - 2; i >= 0; i--)
                {
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, _upFbo[i]);
                    GL.Viewport(0, 0, _levelW[i], _levelH[i]);
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, i == _levels - 2 ? _downTex[_levels - 1] : _upTex[i + 1]);
                    GL.ActiveTexture(TextureUnit.Texture1);
                    GL.BindTexture(TextureTarget.Texture2D, _downTex[i]);
                    GL.Uniform2(_uHalf, 0.5f / _levelW[i], 0.5f / _levelH[i]);
                    GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
                }
                bloomTex = _upTex[0];
            }
        }

        // 3. Composite into the window or the offscreen target.
        if (offscreen)
        {
            EnsureOffscreen(_width, _height);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _outFbo);
        }
        else
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }
        GL.Viewport(0, 0, _width, _height);
        GL.UseProgram(_compProg);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _sceneTex);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, bloomTex);
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, _fontTex);
        GL.Uniform1(_cScene, 0);
        GL.Uniform1(_cBloom, 1);
        GL.Uniform1(_cFont, 2);
        GL.Uniform2(_cRes, (float)_width, (float)_height);
        GL.Uniform1(_cBloomStrength, bloom ? p.BloomStrength : 0f);
        GL.Uniform1(_cLineHalf, p.LineHalfWidth);
        GL.Uniform1(_cLineSoft, p.LineSoft ? 1 : 0);
        GL.Uniform1(_cDiag, p.Diagnostics ? 1 : 0);
        GL.Uniform1(_cTextScale, (float)Math.Max(1, _height / 540));
        GL.Uniform1(_cTime, p.Time);
        GL.Uniform1(_cFilmScale, Math.Max(0.5f, _height / 1080f));
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.ActiveTexture(TextureUnit.Texture0);
    }

    /// <summary>Copies the offscreen target to the window, scaled to the given client size.</summary>
    public void BlitOffscreenToWindow(int clientWidth, int clientHeight)
    {
        if (_outFbo == 0 || clientWidth < 1 || clientHeight < 1) return;
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _outFbo);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        GL.BlitFramebuffer(0, 0, _outW, _outH, 0, 0, clientWidth, clientHeight,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>Reads the offscreen target as top-down BGRA rows.</summary>
    public byte[] ReadOffscreen(out int width, out int height)
    {
        width = _outW;
        height = _outH;
        var pixels = new byte[width * height * 4];
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _outFbo);
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.ReadPixels(0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        // GL rows start at the bottom; flip to top-down.
        int stride = width * 4;
        var row = new byte[stride];
        for (int y = 0; y < height / 2; y++)
        {
            int a = y * stride, b = (height - 1 - y) * stride;
            System.Buffer.BlockCopy(pixels, a, row, 0, stride);
            System.Buffer.BlockCopy(pixels, b, pixels, a, stride);
            System.Buffer.BlockCopy(row, 0, pixels, b, stride);
        }
        return pixels;
    }

    void Resize(int width, int height)
    {
        DeleteTargets();
        _width = width;
        _height = height;
        (_sceneTex, _sceneFbo) = CreateTarget(width, height, PixelInternalFormat.R11fG11fB10f, PixelFormat.Rgb, PixelType.Float);

        _levels = 0;
        int w = width, h = height;
        while (_levels < MaxBloomLevels)
        {
            w /= 2;
            h /= 2;
            if (w < 4 || h < 4) break;
            _levelW[_levels] = w;
            _levelH[_levels] = h;
            (_downTex[_levels], _downFbo[_levels]) = CreateTarget(w, h, PixelInternalFormat.R11fG11fB10f, PixelFormat.Rgb, PixelType.Float);
            (_upTex[_levels], _upFbo[_levels]) = CreateTarget(w, h, PixelInternalFormat.R11fG11fB10f, PixelFormat.Rgb, PixelType.Float);
            _levels++;
        }
    }

    void EnsureOffscreen(int width, int height)
    {
        if (_outFbo != 0 && _outW == width && _outH == height) return;
        DeleteOffscreen();
        (_outTex, _outFbo) = CreateTarget(width, height, PixelInternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte);
        _outW = width;
        _outH = height;
    }

    static (int Tex, int Fbo) CreateTarget(int w, int h, PixelInternalFormat internalFormat, PixelFormat format, PixelType type)
    {
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, w, h, 0, format, type, IntPtr.Zero);
        SetSampling(TextureMinFilter.Linear, TextureMagFilter.Linear);
        int fbo = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, tex, 0);
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        if (status != FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException($"Render target {w}x{h} {internalFormat} is not supported by the driver ({status}).");
        return (tex, fbo);
    }

    static void SetSampling(TextureMinFilter min, TextureMagFilter mag)
    {
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)min);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)mag);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    }

    void DeleteTargets()
    {
        if (_sceneFbo != 0) { GL.DeleteFramebuffer(_sceneFbo); GL.DeleteTexture(_sceneTex); _sceneFbo = _sceneTex = 0; }
        for (int i = 0; i < _levels; i++)
        {
            GL.DeleteFramebuffer(_downFbo[i]); GL.DeleteTexture(_downTex[i]);
            GL.DeleteFramebuffer(_upFbo[i]); GL.DeleteTexture(_upTex[i]);
            _downFbo[i] = _downTex[i] = _upFbo[i] = _upTex[i] = 0;
        }
        _levels = 0;
        _width = _height = 0;
    }

    void DeleteOffscreen()
    {
        if (_outFbo != 0) { GL.DeleteFramebuffer(_outFbo); GL.DeleteTexture(_outTex); _outFbo = _outTex = 0; }
    }

    public void Dispose()
    {
        DeleteTargets();
        DeleteOffscreen();
        GL.DeleteTexture(_fontTex);
        GL.DeleteVertexArray(_vao);
        GL.DeleteProgram(_sceneProg);
        GL.DeleteProgram(_downProg);
        GL.DeleteProgram(_upProg);
        GL.DeleteProgram(_compProg);
    }

    static string LoadShader(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shaders." + name)
            ?? throw new InvalidOperationException($"Embedded shader {name} is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static int BuildProgram(string vertSource, string fragSource, string name)
    {
        int vs = Compile(ShaderType.VertexShader, vertSource, name);
        int fs = Compile(ShaderType.FragmentShader, fragSource, name);
        int prog = GL.CreateProgram();
        GL.AttachShader(prog, vs);
        GL.AttachShader(prog, fs);
        GL.LinkProgram(prog);
        GL.GetProgram(prog, GetProgramParameterName.LinkStatus, out int ok);
        GL.DetachShader(prog, vs);
        GL.DetachShader(prog, fs);
        GL.DeleteShader(vs);
        GL.DeleteShader(fs);
        if (ok == 0)
        {
            string info = GL.GetProgramInfoLog(prog);
            GL.DeleteProgram(prog);
            throw new InvalidOperationException($"Shader program {name} failed to link: {info}");
        }
        return prog;
    }

    static int Compile(ShaderType type, string source, string name)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0)
        {
            string info = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new InvalidOperationException($"Shader {name} ({type}) failed to compile: {info}");
        }
        return shader;
    }
}
