﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Xml.Linq;
using System.Xml;
using SharpGL.Shaders;
using SharpGL;
using WooFractal.Objects;

namespace WooFractal
{
    public enum EFractalType
    {
        Tetra = 0,
        Menger = 1,
        Cube = 2
    }
    public abstract class WooFractalIteration
    {
        public abstract UserControl GetControl();
        public abstract void SetDeclarations(ref ShaderVariables shaderVars);
        public abstract void CompileDeclerations(ref string frag, int iteration);
        public abstract void Compile(ref string frag, int iteration);
        public abstract void CreateElement(XElement parent);
        public int _Repeats = 1;
    };


}
