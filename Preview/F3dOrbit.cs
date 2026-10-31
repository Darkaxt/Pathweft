// Selected math from F3D4.0.0 vtkF3DInteractorStyle::Rotate, BSD-3-Clause.
// Keep native fixed-up/pole behavior without its per-input rwi->Render().
// Copyright F3D contributors; see vendor/F3D/NOTICES.txt.
using System.Windows.Media.Media3D;

namespace FileExplorer.Preview;
internal sealed class F3dOrbit(double[] modelUp)
{
    readonly Vector3D worldUp=Normalized(new(modelUp[0],modelUp[1],modelUp[2]));
    Vector3D temporaryUp=Normalized(new(modelUp[0],modelUp[1],modelUp[2]));
    double temporaryFactor=1;
    static Vector3D Normalized(Vector3D value){value.Normalize();return value;}
    static Vector3D Rotate(Vector3D value,Vector3D axis,double degrees)
    {
        double angle=degrees*Math.PI/180,c=Math.Cos(angle),s=Math.Sin(angle);
        return value*c+Vector3D.CrossProduct(axis,value)*s+axis*(Vector3D.DotProduct(axis,value)*(1-c));
    }
    Vector3D InterpolatedUp()
    {
        temporaryFactor=Math.Max(temporaryFactor-.1,0);
        double factor=(1-Math.Cos(Math.PI*temporaryFactor))*.5;
        return Normalized(temporaryUp*factor+worldUp*(1-factor));
    }
    internal void Apply(IntPtr camera,int dx,int dy,int width,int height,bool pan)
    {
        F3dNative.f3d_camera_get_state(camera,out var state);
        var position=new Vector3D(state.X,state.Y,state.Z);var focal=new Vector3D(state.FX,state.FY,state.FZ);
        var offset=position-focal;var direction=Normalized(-offset);
        if(pan)
        {
            double span=2*offset.Length*Math.Tan(state.Angle*Math.PI/360);
            F3dNative.f3d_camera_pan(camera,-dx*span/height,-dy*span/height,0);return;
        }
        var up=InterpolatedUp();
        const double epsilon=128*2.2204460492503131e-16;
        if(Vector3D.CrossProduct(up,direction).Length<epsilon)
        {temporaryUp=new(state.UX,state.UY,state.UZ);temporaryFactor=1;up=InterpolatedUp();}
        // Native yaw uses the fixed environment axis; elevation uses the
        // camera-right axis and is clamped before crossing either pole.
        offset=Rotate(offset,worldUp,-dx*200.0/width);
        // vtkMath::AngleBetweenVectors uses atan2(cross length,dot), retaining
        // the tiny distance to a pole that acos(dot) rounds away.
        double elevation=(Math.Atan2(Vector3D.CrossProduct(worldUp,direction).Length,Vector3D.DotProduct(worldUp,direction))-Math.PI/2)*180/Math.PI;
        const double limit=90-1e-10;
        double pitch=Math.Clamp(-dy*200.0/height,-limit-elevation,limit-elevation);
        direction=Normalized(-offset);
        offset=Rotate(offset,Normalized(Vector3D.CrossProduct(up,direction)),pitch);
        position=focal+offset;direction=Normalized(-offset);
        up=Normalized(Vector3D.CrossProduct(Normalized(Vector3D.CrossProduct(direction,up)),direction));
        state.X=position.X;state.Y=position.Y;state.Z=position.Z;
        state.UX=up.X;state.UY=up.Y;state.UZ=up.Z;
        F3dNative.f3d_camera_set_state(camera,ref state);
    }
}
