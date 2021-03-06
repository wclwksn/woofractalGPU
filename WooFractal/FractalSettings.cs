﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.Xml.Linq;
using System.Xml;
using System.IO;
using WooFractal.Objects;

namespace WooFractal
{
    public class FractalSettings
    {
        public FractalSettings()
        {
            _FractalIterations = new List<WooFractalIteration>();
            _RenderOptions = new RenderOptions();
            _FractalColours = new List<FractalGradient>();
            FractalGradient fractalGradient = new FractalGradient();
            _FractalColours.Add(fractalGradient);
            _MaterialSelection = new MaterialSelection();
        }

        public void Set(RenderOptions renderOptions, List<FractalGradient> fractalColours, List<WooFractalIteration> fractalIterations, MaterialSelection materialSelection)
        {
            _RenderOptions = renderOptions;
            _FractalColours = fractalColours;
            _FractalIterations = fractalIterations;
            _MaterialSelection = materialSelection;
        }
        public void CreateElement(XElement parent)
        {
            XElement ret = new XElement("FRACTAL");
            _RenderOptions.CreateElement(ret);
            for (int i = 0; i < _FractalColours.Count; i++)
                _FractalColours[i].CreateElement(ret);
            for (int i = 0; i < _FractalIterations.Count; i++)
                _FractalIterations[i].CreateElement(ret);
            _MaterialSelection.CreateElement(ret);
            parent.Add(ret);
        }
        public void LoadXML(XmlReader reader)
        {
            _FractalColours.Clear();
            _FractalIterations = new List<WooFractalIteration>();
            while (reader.NodeType != XmlNodeType.EndElement && reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "MATERIALSELECTION")
                {
                    _MaterialSelection = new MaterialSelection();
                    _MaterialSelection.LoadXML(reader);
                }
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "RENDEROPTIONS")
                {
                    _RenderOptions = new RenderOptions();
                    _RenderOptions.LoadXML(reader);
                }
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "FRACTALCOLOURS")
                {
                    FractalGradient fractalColour = new FractalGradient();
                    fractalColour.LoadXML(reader);
                    _FractalColours.Add(fractalColour);
                }
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "KIFSFRACTAL")
                {
                    KIFSIteration fractalIteration = new KIFSIteration();
                    fractalIteration.LoadXML(reader);
                    _FractalIterations.Add(fractalIteration);
                }
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "BULBFRACTAL")
                {
                    MandelbulbIteration fractalIteration = new MandelbulbIteration();
                    fractalIteration.LoadXML(reader);
                    _FractalIterations.Add(fractalIteration);
                }
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "BOXFRACTAL")
                {
                    MandelboxIteration fractalIteration = new MandelboxIteration();
                    fractalIteration.LoadXML(reader);
                    _FractalIterations.Add(fractalIteration);
                }
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "KLEINIANGROUP")
                {
                    KleinianGroupIteration fractalIteration = new KleinianGroupIteration();
                    fractalIteration.LoadXML(reader);
                    _FractalIterations.Add(fractalIteration);
                }
            }
            reader.Read();
        }
        public void CompileColours(ref string frag)
        {
            int divisor = _FractalColours.Count();

            int maxSegments = 0;
            for (int i = 0; i < _FractalColours.Count(); i++)
            {
                int segCount = _FractalColours[i]._GradientSegments.Count();
                if (segCount > maxSegments)
                    maxSegments = segCount;
                frag += @"
uniform vec3 diffStart"+i.ToString()+"[" + segCount + @"];
uniform vec3 diffEnd" + i.ToString() + "[" + segCount + @"];
uniform vec3 specStart" + i.ToString() + "[" + segCount + @"];
uniform vec3 specEnd" + i.ToString() + "[" + segCount + @"];
uniform vec3 reflStart" + i.ToString() + "[" + segCount + @"];
uniform vec3 reflEnd" + i.ToString() + "[" + segCount + @"];
uniform float roughStart" + i.ToString() + "[" + segCount + @"];
uniform float roughEnd" + i.ToString() + "[" + segCount + @"];
uniform float dlcStart" + i.ToString() + "[" + segCount + @"];
uniform float dlcEnd" + i.ToString() + "[" + segCount + @"];
uniform float segStart" + i.ToString() + "[" + segCount + @"];
uniform float segEnd" + i.ToString() + "[" + segCount + @"];
uniform float orbitType" + i.ToString() + @";
uniform float multiplier" + i.ToString() + @";
uniform float offset" + i.ToString() + @";
uniform float segments" + i.ToString() + @";

";
            }

            frag += @"
uniform float divisor;

float GetTrapPos(vec4 orbitTrap, float orbitType, float multiplier, float offset)
{
 float val = 0;
 if (orbitType==0) val = orbitTrap.x;
 if (orbitType==1) val = orbitTrap.y;
 if (orbitType==2) val = orbitTrap.z;
 if (orbitType==3) val = orbitTrap.w;
 return mod((val*multiplier)+offset,1.0);
}

void OrbitToColour(in vec4 orbitTrap, inout material mat)
{
mat.diff = vec3(0,0,0);
mat.spec = vec3(0,0,0);
mat.refl = vec3(0,0,0);
mat.dlc = 0.0;
mat.roughness = 0.0;

float trappos;

int currS;
float gradX;";
            for (int i=0; i<_FractalColours.Count(); i++)
            {
                frag += @"
trappos = GetTrapPos(orbitTrap, orbitType" + i.ToString() + @", multiplier" + i.ToString() + @", offset" + i.ToString() + @");
currS = 0;
for (int i=1; i<segments" + i.ToString() + @"; i++)
{
 currS = int(max(currS, i * min(1.0, floor(1.0+(trappos-segStart" + i.ToString() + @"[i])) * min(1.0, floor(1.0+(segEnd" + i.ToString() + @"[i]-trappos))))));
}

gradX = (trappos - segStart" + i.ToString() + @"[currS]) / (segEnd" + i.ToString() + @"[currS] - segStart" + i.ToString() + @"[currS]);
mat.diff+=mix(diffStart" + i.ToString() + @"[currS], diffEnd" + i.ToString() + @"[currS], gradX);
mat.spec+=mix(specStart" + i.ToString() + @"[currS], specEnd" + i.ToString() + @"[currS], gradX);
mat.refl+=mix(reflStart" + i.ToString() + @"[currS], reflEnd" + i.ToString() + @"[currS], gradX);
mat.dlc+=mix(dlcStart" + i.ToString() + @"[currS], dlcEnd" + i.ToString() + @"[currS], gradX);
mat.roughness+=mix(roughStart" + i.ToString() + @"[currS], roughEnd" + i.ToString() + @"[currS], gradX);
";
            }

            frag += @"
mat.diff/=divisor;
mat.spec/=divisor;
mat.refl/=divisor;
mat.dlc/=divisor;
mat.roughness/=divisor;
}
";
        }

        public void Compile(ref string frag)
        {
            string frag2 = @"

void Menger(inout vec3 pos, in vec3 origPos, inout float scale, in float mScale, in vec3 mOffset, in mat3 mRotate1Matrix, in mat3 mRotate2Matrix)
{
	pos *= mRotate1Matrix;
    vec3 mPOffset = mOffset*(mScale-1);
	float tmp;

	pos = abs(pos);
	if(pos[0]-pos[1]<0){tmp=pos[1];pos[1]=pos[0];pos[0]=tmp;}
	if(pos[0]-pos[2]<0){tmp=pos[2];pos[2]=pos[0];pos[0]=tmp;}
	if(pos[1]-pos[2]<0){tmp=pos[2];pos[2]=pos[1];pos[1]=tmp;}
      
	pos[2]-=0.5f*mOffset[2]*(mScale-1)/mScale;
	pos[2]=-abs(-pos[2]);
	pos[2]+=0.5f*mOffset[2]*(mScale-1)/mScale;
      
	pos *= mRotate2Matrix;

	pos[0]=mScale*pos[0]-mPOffset[0];
	pos[1]=mScale*pos[1]-mPOffset[1];
	pos[2]=mScale*pos[2];

	scale *= mScale;
}

void Tetra(inout vec3 pos, in vec3 origPos, inout float scale, in float mScale, in vec3 mPOffset, in mat3 mRotate1Matrix, in mat3 mRotate2Matrix)
{
	float tmp;

	pos *= mRotate1Matrix;

	if(pos[0]+pos[1]<0){tmp=-pos[1];pos[1]=-pos[0];pos[0]=tmp;}
	if(pos[0]+pos[2]<0){tmp=-pos[2];pos[2]=-pos[0];pos[0]=tmp;}
	if(pos[1]+pos[2]<0){tmp=-pos[2];pos[2]=-pos[1];pos[1]=tmp;}
      
	pos *= mRotate2Matrix;

	pos = pos*mScale - mPOffset;
	scale *= mScale;
}

void Bulb(in float r, inout vec3 pos, in vec3 origPos, inout float scale, in float mScale, in mat3 mRotate1Matrix, in float juliaEnabled, in vec3 julia)
{
    float theta, phi;
    
	theta = atan(pos.y / pos.x);
	phi = asin(pos.z / r);
	scale =  pow( r, mScale-1.0)*mScale*scale + 1.0;

	// scale and rotate the point
	float zr = pow( r,mScale);
	theta = theta*mScale;
	phi = phi*mScale;

	// convert back to cartesian coordinates
	pos = zr * vec3(cos(theta)*cos(phi), sin(theta)*cos(phi), sin(phi));

    // julia Land?
    pos += (juliaEnabled>0.5) ? julia : origPos;

	pos = mRotate1Matrix * pos;
}

void Kleinian(inout vec3 pos, in vec3 origPos, inout float scale, in float mScale, in vec3 mCSize, in vec3 mJulia)
{
	pos = 2.0*clamp(pos, -mCSize, mCSize) - pos;

	float r2 = dot(pos, pos);
	float k = max(mScale/r2, 1);
	pos *= k;
	scale *= k;

	pos += mJulia;
}

void Box(inout vec3 pos, in vec3 origPos, inout float scale, in vec3 mScale, in mat3 mRotate1Matrix, in float mMinRadius )
{
	pos *= mRotate1Matrix;
	float fixedRadius = 1.0;
	float fR2 = fixedRadius * fixedRadius;
	float mR2 = mMinRadius * mMinRadius;

    pos = clamp(pos, -1, 1) *2.0 - pos;		

	float r2 = dot(pos,pos);

	if (r2 < mR2)
	{
		pos *= fR2 / mR2;
		scale*= fR2 / mR2;
	}
	else if (r2 < fR2)
	{
		pos *= fR2 / r2;
		scale*= fR2 / r2;
	}
			
    pos = (pos * mScale) + origPos;
    if (dot(mScale,vec3(1))>0)
    	scale = scale * abs(max(mScale.x, max(mScale.y,mScale.z))) + 1.0f;
    else
    	scale = scale * abs(min(mScale.x, min(mScale.y,mScale.z))) + 1.0f;
}

void Cuboid(inout vec3 pos, in vec3 origPos, inout float scale, in float mScale, in vec3 mPOffset, in mat3 mRotate1Matrix, in mat3 mRotate2Matrix )
{
	pos *= mRotate1Matrix;

	pos = abs(pos);

	pos *= mRotate2Matrix;

	pos = pos*mScale - mPOffset;
	scale *= mScale;
}";
            for (int i = 0; i < _FractalIterations.Count; i++)
            {
                _FractalIterations[i].CompileDeclerations(ref frag2, i);
            }
            frag2 += @"

float DE(in vec3 origPos, out vec4 orbitTrap)
{
  origPos.xyz = origPos.xzy;
  vec3 pos = origPos;
  float r, theta, phi;
  float scale = 1;
  float mScale = 8;
  float fracIterations = 0;
  int DEMode = 0;
  orbitTrap = vec4(10000,10000,10000,10000);
  
  for (int j=0; j<" + _RenderOptions._FractalIterationCount+@"; j++)
  {
    r = length(pos);
    if (r>40.0) continue;
    if (j<"+_RenderOptions._ColourIterationCount+@") orbitTrap = min(orbitTrap, vec4(abs(pos),r));";

            int totalIterations = 0,iterationIndex=0;
            for (int i=0; i<_FractalIterations.Count; i++)
            {
                totalIterations += _FractalIterations[i]._Repeats;
            }

            frag2+=@"
 int modj = j%"+totalIterations+@";";

            for (int i = 0; i < _FractalIterations.Count; i++)
            {
                int repeats = _FractalIterations[i]._Repeats;
                frag2 += @"
 if (modj>=" + iterationIndex + " && modj<" + (iterationIndex + repeats).ToString() + @")
 {";
                _FractalIterations[i].Compile(ref frag2, i);
                frag2 += @"
}";
                iterationIndex += repeats;
            }

            frag2 += @"
  }
 //r = length(pos);
    float ret=0;
 // DEMode 0=KIFS, 1=BOX, 2=BULB, 3=kleinian
 if (DEMode==1) ret = (r - 1) / abs(scale);
 if (DEMode==2) ret = 0.5*log(r)*r/scale;
 if (DEMode==0) ret = (r - 1) / abs(scale);
 if (DEMode==3) ret = 0.5*abs(pos.z)/scale;
 float bbdist = length(origPos - clamp(origPos, vec3(-distanceExtents), vec3(distanceExtents)));
 ret = max(ret, bbdist);
 return ret;
}

// https://github.com/hpicgs/cgsee/wiki/Ray-Box-Intersection-on-the-GPU
void intersectAABB(in vec3 pos, in vec3 dir, out float tmin, out float tmax)
{
 float tymin, tymax, tzmin, tzmax;
 vec3 invdir = vec3(1)/(dir);
 vec3 sign = vec3(dir.x>=0?distanceExtents:-distanceExtents, dir.y>=0?distanceExtents:-distanceExtents, dir.z>=0?distanceExtents:-distanceExtents);
 tmin = (-sign.x - pos.x) * invdir.x;
 tmax = (sign.x - pos.x) * invdir.x;
 tymin = (-sign.y - pos.y) * invdir.y;
 tymax = (sign.y - pos.y) * invdir.y;
 tzmin = (-sign.z - pos.z) * invdir.z;
 tzmax = (sign.z - pos.z) * invdir.z;
 tmin = max(max(tmin, tymin), tzmin);
 tmax = min(min(tmax, tymax), tzmax);   
}

bool traceFractal(in vec3 pos, in vec3 dir, inout float dist, out vec3 out_pos, out vec3 normal, out material mat)
{
mat.diff = vec3(1,1,1);
mat.spec = vec3(0.2,0.2,0.2);
mat.refl = vec3(0.2,0.2,0.2);
mat.roughness = 0.01;
  pos.y -= distanceExtents;
  vec3 srcPos = camPos;
  srcPos.y -= distanceExtents;
  float minDistance = " + Math.Pow(10, -_RenderOptions._DistanceMinimum).ToString("0.#######") + @";
  
  // clip to AABB
  float tmin, tmax;
  intersectAABB(pos, dir, tmin, tmax);

  // bail if no collision
  if (tmin>tmax) return false;
  
  // skip ray to start of AABB
  tmin = max(0, tmin);
  vec3 dp = pos + tmin*dir;

  vec4 orbitTrap = vec4(10000,10000,10000,10000);
  float DEdist;
  vec3 oldDp;
  float minDistance2 = minDistance;

  // iterate...
  for (int i=0; i<" + _RenderOptions._DistanceIterations + @"; i++)
  {
   DEdist = DE(dp, orbitTrap);
   oldDp = dp;
   dp += " + _RenderOptions._StepSize + @"*DEdist*dir;
   tmax -= " + _RenderOptions._StepSize + @"*DEdist; // not sure this is the most efficient tbh
   if (tmax<0) return false; // exiting the AABB, skip
";

   if (_RenderOptions._DistanceMinimumMode!=0)
       frag2 += "minDistance2 = length(dp-srcPos) / screenWidth;";
   
            frag2 += @"
   if (DEdist<minDistance2)
   {
    vec3 mid;
    vec4 torbitTrap;
    vec3 midInside;
    for (int j=0; j<4; j++)
    {
     mid = (dp+oldDp)*0.5;
";

            if (_RenderOptions._DistanceMinimumMode != 0)
                frag2 += "minDistance2 = length(mid-srcPos) / screenWidth;";

            frag2 += @"
     midInside.x = max(0,sign(DE(mid, torbitTrap)-minDistance2));
     midInside.y = 1-midInside.x;
     dp = midInside.xxx*dp + midInside.yyy*mid;
     orbitTrap = midInside.xxxx*torbitTrap + midInside.yyyy*orbitTrap;
     oldDp = midInside.xxx*mid + midInside.yyy*oldDp;
    }

    OrbitToColour(orbitTrap, mat);
	vec3 normalTweak=vec3(minDistance2*0.1f,0,0);
	normal = vec3(DE(dp+normalTweak.xyy,orbitTrap) - DE(dp-normalTweak.xyy,orbitTrap),
		DE(dp+normalTweak.yxy,orbitTrap) - DE(dp-normalTweak.yxy,orbitTrap),
		DE(dp+normalTweak.yyx,orbitTrap) - DE(dp-normalTweak.yyx,orbitTrap));
    float magSq = dot(normal, normal);
    if (magSq<=0.001*minDistance2*minDistance2)
        normal = -dir;
    else
        normal /= sqrt(magSq);

    out_pos = dp + normal*(4*minDistance2) + vec3(0,distanceExtents,0);
    dist = length(dp - pos);
    return true;
   }
  }
  return false;
}
";
            frag += frag2;
        }

        public void SetFractalDeclerations(ref ShaderVariables shaderVariables)
        {
            for (int i = 0; i < _FractalIterations.Count; i++)
            {
                _FractalIterations[i].SetDeclarations(ref shaderVariables);
            }
        }

        public void SetColourDeclerations(ref ShaderVariables shaderVariables)
        {
            shaderVariables.Add("divisor", _FractalColours.Count);

            for (int i = 0; i < _FractalColours.Count; i++)
            {
                _FractalColours[i].SetDeclarations(ref shaderVariables, i);
            }
        }

        public RenderOptions _RenderOptions;
        public List<FractalGradient> _FractalColours;
        public List<WooFractalIteration> _FractalIterations;
        public MaterialSelection _MaterialSelection;
    }
}
