#include "ofxImageEffect.h"
#include "ofxInteract.h"
#include "ofxProperty.h"
#include "ofxParam.h"
#include "ofxMessage.h"
#include "CalibrationAbi.h"
#include "LogTransform.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <dlfcn.h>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>

#define GL_SILENCE_DEPRECATION
#include <OpenGL/gl.h>

namespace {
OfxHost* host = nullptr;
const OfxPropertySuiteV1* props = nullptr;
const OfxImageEffectSuiteV1* effects = nullptr;
const OfxParameterSuiteV1* params = nullptr;
const OfxMessageSuiteV1* messages = nullptr;
const OfxInteractSuiteV1* interacts = nullptr;
const char* minNames[] = {"dminR", "dminG", "dminB"};
const char* maxNames[] = {"dmaxR", "dmaxG", "dmaxB"};
const char* gainNames[] = {"slopeR", "slopeG", "slopeB"};
const char* shiftNames[] = {"shiftR", "shiftG", "shiftB"};
const char* gammaNames[] = {"midtoneR", "midtoneG", "midtoneB"};
const char* roiNames[] = {"roiLeft", "roiBottom", "roiWidth", "roiHeight"};
const char* neutralRoiNames[] = {"neutralLeft", "neutralBottom", "neutralWidth", "neutralHeight"};
void check(OfxStatus s) { if (s != kOfxStatOK) throw std::runtime_error("OFX host call failed: " + std::to_string(s)); }
std::string stringProp(OfxPropertySetHandle p, const char* name) {
    char* s = nullptr; check(props->propGetString(p, name, 0, &s)); return s ? s : "";
}
void notify(OfxImageEffectHandle effect, const std::string& s) {
    if (messages) messages->message(effect, kOfxMessageError, "revelare", "%s", s.c_str());
}
struct Instance {
    OfxImageClipHandle source{}, output{};
    OfxParamSetHandle set{};
    OfxParamHandle enabled{}, lock{}, status{}, neutralStatus{}, neutralTarget{}, neutralAbsolute{}, neutralOverlay{}, neutralSize{}, samplePosition{}, whiteCode{}, endpointSchema{};
    std::array<OfxParamHandle, 3> dmin{}, dmax{}, slope{}, shift{}, gamma{};
    std::array<OfxParamHandle, 4> roi{}, neutralRoi{};
};
OfxParamHandle parameter(OfxParamSetHandle set, const char* name) {
    OfxParamHandle p{}; check(params->paramGetHandle(set, name, &p, nullptr)); return p;
}
Instance* instance(OfxImageEffectHandle effect) {
    OfxPropertySetHandle p{}; check(effects->getPropertySet(effect, &p));
    void* v = nullptr; check(props->propGetPointer(p, kOfxPropInstanceData, 0, &v));
    if (!v) throw std::runtime_error("Missing effect instance.");
    return static_cast<Instance*>(v);
}
double number(OfxParamHandle p, double time) { double v; check(params->paramGetValueAtTime(p,time,&v)); return v; }
int integer(OfxParamHandle p, double time) { int v; check(params->paramGetValueAtTime(p,time,&v)); return v; }
std::array<double,2> point(OfxParamHandle p, double time) {
    std::array<double,2> v{}; check(params->paramGetValueAtTime(p,time,&v[0],&v[1])); return v;
}
void updateLock(Instance& i, double time) {
    int locked = integer(i.lock,time);
    for (auto p : i.dmin) {
        OfxPropertySetHandle properties{}; check(params->paramGetPropertySet(p,&properties));
        check(props->propSetInt(properties,kOfxParamPropEnabled,0,!locked));
    }
}
// UI bounds follow the calibrated channel, rather than an unrelated global range.
// Render still validates independently: restored projects and undo can contain old values.
void updateEndpointControls(Instance& i, double time) {
    for (int c=0;c<3;c++) {
        double lo=number(i.dmin[c],time), hi=number(i.dmax[c],time);
        if (!std::isfinite(lo) || !std::isfinite(hi)) continue;
        double span=std::max(hi-lo,0.001);
        OfxPropertySetHandle p{}; check(params->paramGetPropertySet(i.dmax[c],&p));
        check(props->propSetDouble(p,kOfxParamPropMin,0,lo+0.001));
        check(props->propSetDouble(p,kOfxParamPropDisplayMin,0,lo+std::max(0.001,span*0.5)));
        check(props->propSetDouble(p,kOfxParamPropDisplayMax,0,lo+span*2.0));
    }
}
void validateStatus(Instance& i, double time) {
    double lo[3],hi[3],gain[3],shift[3],gamma[3];
    for(int c=0;c<3;c++){lo[c]=number(i.dmin[c],time);hi[c]=number(i.dmax[c],time);gain[c]=number(i.slope[c],time);shift[c]=number(i.shift[c],time);gamma[c]=number(i.gamma[c],time);}
    if(!revelare::validEndpoints(lo,hi,gain,shift,gamma))
        params->paramSetValue(i.status,"Invalid endpoints: inversion bypassed. Dmax must exceed Dmin; re-analyze or correct Dmax.");
    else params->paramSetValue(i.status,"Endpoints valid. Manual adjustment; Dmin lock is unchanged.");
}
struct Image {
    OfxPropertySetHandle handle{};
    char* data{}; int bounds[4]{}, rowBytes{}, channels{}; bool premult{};
    Image(OfxImageClipHandle clip, double time, const OfxRectD* region=nullptr) {
        check(effects->clipGetImage(clip,time,region,&handle));
        try {
            if (stringProp(handle,kOfxImageEffectPropPixelDepth) != kOfxBitDepthFloat)
                throw std::runtime_error("Revelare requires float32 images.");
            auto components = stringProp(handle,kOfxImageEffectPropComponents);
            channels = components==kOfxImageComponentRGBA ? 4 : components==kOfxImageComponentRGB ? 3 : 0;
            if (!channels) throw std::runtime_error("Revelare requires RGB or RGBA images.");
            void* v{}; check(props->propGetPointer(handle,kOfxImagePropData,0,&v)); data=static_cast<char*>(v);
            check(props->propGetIntN(handle,kOfxImagePropBounds,4,bounds));
            check(props->propGetInt(handle,kOfxImagePropRowBytes,0,&rowBytes));
            char* p=nullptr;
            if (props->propGetString(handle,kOfxImageEffectPropPreMultiplication,0,&p)==kOfxStatOK && p)
                premult=std::strcmp(p,kOfxImagePreMultiplied)==0;
            if (!data) throw std::runtime_error("Host supplied no CPU pixel buffer.");
        } catch (...) { effects->clipReleaseImage(handle); handle=nullptr; throw; }
    }
    ~Image() { if(handle) effects->clipReleaseImage(handle); }
    Image(const Image&)=delete;
    float* pixel(int x,int y) const {
        if(x<bounds[0]||x>=bounds[2]||y<bounds[1]||y>=bounds[3]) return nullptr;
        return reinterpret_cast<float*>(data + static_cast<ptrdiff_t>(y-bounds[1])*rowBytes)+(x-bounds[0])*channels;
    }
};
AnalyzeSingle loadCalibration() {
    static std::mutex mutex;
    static AnalyzeSingle fn=nullptr;
    static void* library=nullptr; // NativeAOT runtime stays loaded until process exit.
    std::lock_guard<std::mutex> guard(mutex);
    if(fn) return fn;
    if(!library) {
        Dl_info info{};
        if(!dladdr(reinterpret_cast<void*>(&loadCalibration),&info) || !info.dli_fname)
            throw std::runtime_error("Cannot locate Revelare bundle.");
        std::string path=info.dli_fname;
        path=path.substr(0,path.find_last_of('/'))+"/OpenRevelare.Calibration.Native.dylib";
        library=dlopen(path.c_str(),RTLD_NOW|RTLD_LOCAL);
        if(!library) throw std::runtime_error(std::string("Cannot load calibration library: ")+dlerror());
    }
    auto abi=reinterpret_cast<CalibrationAbi>(dlsym(library,"or_calibration_abi"));
    auto analyze=reinterpret_cast<AnalyzeSingle>(dlsym(library,"or_analyze_single"));
    if(!abi || abi()!=1 || !analyze) throw std::runtime_error("Calibration ABI mismatch.");
    fn=analyze; return fn;
}
OfxPropertySetHandle define(OfxParamSetHandle set,const char* type,const char* name,const char* label) {
    OfxPropertySetHandle p{}; check(params->paramDefine(set,type,name,&p));
    check(props->propSetString(p,kOfxPropLabel,0,label));
    // Calibration is fixed, not a time-varying estimate; host owns persistence/undo.
    if(std::strcmp(type,kOfxParamTypePushButton)!=0) props->propSetInt(p,kOfxParamPropAnimates,0,0);
    return p;
}
OfxPropertySetHandle defineNumber(OfxParamSetHandle set,const char* name,const char* label,double value,double lo,double hi,
                  double increment=0.001,int digits=4) {
    auto p=define(set,kOfxParamTypeDouble,name,label);
    check(props->propSetDouble(p,kOfxParamPropDefault,0,value));
    check(props->propSetDouble(p,kOfxParamPropMin,0,lo));
    check(props->propSetDouble(p,kOfxParamPropMax,0,hi));
    check(props->propSetDouble(p,kOfxParamPropDisplayMin,0,lo));
    check(props->propSetDouble(p,kOfxParamPropDisplayMax,0,hi));
    props->propSetDouble(p,kOfxParamPropIncrement,0,increment);
    props->propSetInt(p,kOfxParamPropDigits,0,digits);
    return p;
}
OfxStatus overlayEntry(const char* action,const void* handle,OfxPropertySetHandle in,OfxPropertySetHandle out);
OfxStatus describe(OfxImageEffectHandle effect) {
    OfxPropertySetHandle p{}; check(effects->getPropertySet(effect,&p));
    check(props->propSetString(p,kOfxPropLabel,0,"Revelare Negative (Prototype)"));
    check(props->propSetString(p,kOfxImageEffectPluginPropGrouping,0,"Film Negative"));
    check(props->propSetString(p,kOfxImageEffectPropSupportedContexts,0,kOfxImageEffectContextFilter));
    check(props->propSetString(p,kOfxImageEffectPropSupportedPixelDepths,0,kOfxBitDepthFloat));
    check(props->propSetInt(p,kOfxImageEffectPropSupportsTiles,0,0));
    check(props->propSetInt(p,kOfxImageEffectPropTemporalClipAccess,0,0));
    check(props->propSetString(p,kOfxImageEffectPluginRenderThreadSafety,0,kOfxImageEffectRenderFullySafe));
    // Resolve supports the standard OpenFX viewer overlay. It gives the neutral
    // sampler a visible, draggable region instead of four blind numeric fields.
    check(props->propSetPointer(p,kOfxImageEffectPluginPropOverlayInteractV1,0,reinterpret_cast<void*>(overlayEntry)));
    return kOfxStatOK;
}
OfxStatus describeContext(OfxImageEffectHandle effect) {
    for(auto name : {kOfxImageEffectSimpleSourceClipName,kOfxImageEffectOutputClipName}) {
        OfxPropertySetHandle p{}; check(effects->clipDefine(effect,name,&p));
        check(props->propSetString(p,kOfxImageEffectPropSupportedComponents,0,kOfxImageComponentRGBA));
        check(props->propSetString(p,kOfxImageEffectPropSupportedComponents,1,kOfxImageComponentRGB));
        props->propSetInt(p,kOfxImageEffectPropSupportsTiles,0,0);
    }
    OfxParamSetHandle set{}; check(effects->getParamSet(effect,&set));
    auto p=define(set,kOfxParamTypeBoolean,"enabled","启用反相 / Enable inversion");
    check(props->propSetInt(p,kOfxParamPropDefault,0,0));
    props->propSetString(p,kOfxParamPropHint,0,"Input must be linear negative RGB. Output retains input primaries, Cineon-style Log. No Rec.709 conversion. Analyze enables inversion.");
    define(set,kOfxParamTypePushButton,"analyze","自动单张 / Analyze frame");
    p=define(set,kOfxParamTypeString,"analysisStatus","标定状态 / Analysis status");
    check(props->propSetString(p,kOfxParamPropDefault,0,"Not calibrated. Linear input required; primaries are preserved."));
    props->propSetInt(p,kOfxParamPropEnabled,0,0);
    p=define(set,kOfxParamTypeBoolean,"lockBase","锁定 Dmin / Lock film base");
    check(props->propSetInt(p,kOfxParamPropDefault,0,0));
    for(int c=0;c<3;c++) {
        defineNumber(set,minNames[c],minNames[c],0.0,-4.0,4.0);
        defineNumber(set,maxNames[c],maxNames[c],2.0,-3.9,8.0);
        defineNumber(set,gainNames[c],gainNames[c],1.0,0.25,4.0);
        defineNumber(set,shiftNames[c],shiftNames[c],0.0,-256.0,256.0,1.0,1);
        defineNumber(set,gammaNames[c],gammaNames[c],1.0,0.20,5.0,0.001,4);
    }
    // Analyze estimates the negative's density endpoint, so its safe default is the
    // true-Dmax placement. Code 685 is only correct when the user deliberately
    // identifies a diffuse-white sample rather than a density endpoint.
    defineNumber(set,"whiteCode","Dmax 输出码值 / Cineon white placement",1000.0,685.0,1032.0,1.0,0);
    p=define(set,kOfxParamTypePushButton,"diffuseWhite","漫反射白 685 / Diffuse white 685");
    props->propSetString(p,kOfxParamPropHint,0,"Place a sampled diffuse white such as paper or white clothing at Cineon code 685.");
    p=define(set,kOfxParamTypePushButton,"trueDmax","真实 Dmax 1000 / True Dmax 1000");
    props->propSetString(p,kOfxParamPropHint,0,"Place a genuine maximum-density sample, such as a specular highlight in the positive, at Cineon code 1000.");
    p=define(set,kOfxParamTypeInteger,"endpointSchema","endpointSchema");
    check(props->propSetInt(p,kOfxParamPropDefault,0,0));
    check(props->propSetInt(p,kOfxParamPropSecret,0,1));
    define(set,kOfxParamTypePushButton,"resetTrim","重置通道调整 / Reset gain + shift + midtone");
    const double roiDefault[]={0.05,0.05,0.90,0.90};
    for(int c=0;c<4;c++) defineNumber(set,roiNames[c],roiNames[c],roiDefault[c],0.0,1.0);
    define(set,kOfxParamTypePushButton,"sampleDmin","框选设为 Dmin / Use box as film base");
    define(set,kOfxParamTypePushButton,"sampleDmax","框选设为 Dmax / Use box as highlight");
    define(set,kOfxParamTypePushButton,"sampleNeutral","框选设为中性灰 / Use box as neutral grey");
    p=define(set,kOfxParamTypeBoolean,"neutralOverlay","显示取样框 / Show sample box");
    check(props->propSetInt(p,kOfxParamPropDefault,0,1));
    props->propSetString(p,kOfxParamPropHint,0,"Resolve draws the native sample-position handle; this switch shows the sampled area around it.");
    p=define(set,kOfxParamTypeDouble2D,"samplePosition","取样位置 / Sample position");
    check(props->propSetString(p,kOfxParamPropDoubleType,0,kOfxParamDoubleTypeXYAbsolute));
    check(props->propSetString(p,kOfxParamPropDefaultCoordinateSystem,0,kOfxParamCoordinatesNormalised));
    check(props->propSetDouble(p,kOfxParamPropDefault,0,0.5));
    check(props->propSetDouble(p,kOfxParamPropDefault,1,0.5));
    // Let Resolve own the viewer handle. It already knows the viewer zoom, pan,
    // pixel aspect and image transforms, which avoids duplicating that mapping here.
    props->propSetInt(p,kOfxParamPropUseHostOverlayHandle,0,1);
    defineNumber(set,"neutralSize","取样大小 / Sample size",0.03,0.005,0.50,0.005,3);
    p=define(set,kOfxParamTypeBoolean,"neutralAbsolute","已知灰卡：放到目标码值 / Known grey card: map to target");
    check(props->propSetInt(p,kOfxParamPropDefault,0,0));
    defineNumber(set,"neutralTarget","灰点 Cineon 码值 / Grey target code",470.0,96.0,1031.0,1.0,0);
    const double neutralDefault[]={0.40,0.40,0.20,0.20};
    for(int c=0;c<4;c++) {
        auto hiddenProps=defineNumber(set,neutralRoiNames[c],neutralRoiNames[c],neutralDefault[c],0.0,1.0);
        check(props->propSetInt(hiddenProps,kOfxParamPropSecret,0,1));
    }
    p=define(set,kOfxParamTypeString,"neutralStatus","中性灰读数 / Neutral reading");
    check(props->propSetString(p,kOfxParamPropDefault,0,"Click a genuinely neutral area in the viewer, then sample."));
    props->propSetInt(p,kOfxParamPropEnabled,0,0);
    return kOfxStatOK;
}
OfxStatus create(OfxImageEffectHandle effect) {
    auto i=std::make_unique<Instance>();
    check(effects->clipGetHandle(effect,kOfxImageEffectSimpleSourceClipName,&i->source,nullptr));
    check(effects->clipGetHandle(effect,kOfxImageEffectOutputClipName,&i->output,nullptr));
    check(effects->getParamSet(effect,&i->set));
    i->enabled=parameter(i->set,"enabled"); i->lock=parameter(i->set,"lockBase"); i->status=parameter(i->set,"analysisStatus");
    i->neutralStatus=parameter(i->set,"neutralStatus");i->neutralTarget=parameter(i->set,"neutralTarget");i->neutralAbsolute=parameter(i->set,"neutralAbsolute");i->neutralOverlay=parameter(i->set,"neutralOverlay");i->neutralSize=parameter(i->set,"neutralSize");i->samplePosition=parameter(i->set,"samplePosition");i->whiteCode=parameter(i->set,"whiteCode");i->endpointSchema=parameter(i->set,"endpointSchema");
    for(int c=0;c<3;c++) {i->dmin[c]=parameter(i->set,minNames[c]);i->dmax[c]=parameter(i->set,maxNames[c]);i->slope[c]=parameter(i->set,gainNames[c]);i->shift[c]=parameter(i->set,shiftNames[c]);i->gamma[c]=parameter(i->set,gammaNames[c]);}
    for(int c=0;c<4;c++){i->roi[c]=parameter(i->set,roiNames[c]);i->neutralRoi[c]=parameter(i->set,neutralRoiNames[c]);}
    // v0 accidentally made diffuse white the default for Analyze. Repair each
    // existing node once; after this marker is saved, an intentional 685 choice
    // remains untouched on later opens.
    if(integer(i->endpointSchema,0)<1) {
        if(std::abs(number(i->whiteCode,0)-685.0)<0.5)
            check(params->paramSetValue(i->whiteCode,1000.0));
        check(params->paramSetValue(i->endpointSchema,1));
    }
    // Repair the oversized drag region produced by the first overlay prototype.
    // Keep its centre so an upgraded project still points at the same subject.
    double oldWidth=number(i->neutralRoi[2],0),oldHeight=number(i->neutralRoi[3],0);
    if(oldWidth>0.5||oldHeight>0.5) {
        double cx=number(i->neutralRoi[0],0)+oldWidth*.5,cy=number(i->neutralRoi[1],0)+oldHeight*.5;
        const double size=number(i->neutralSize,0);
        params->paramSetValue(i->neutralRoi[0],std::clamp(cx-size*.5,0.0,1.0-size));
        params->paramSetValue(i->neutralRoi[1],std::clamp(cy-size*.5,0.0,1.0-size));
        params->paramSetValue(i->neutralRoi[2],size);params->paramSetValue(i->neutralRoi[3],size);
    }
    OfxPropertySetHandle p{}; check(effects->getPropertySet(effect,&p));
    updateLock(*i,0); updateEndpointControls(*i,0);
    check(props->propSetPointer(p,kOfxPropInstanceData,0,i.get()));
    i.release(); return kOfxStatOK;
}
OfxStatus render(OfxImageEffectHandle effect,OfxPropertySetHandle in) {
    auto& i=*instance(effect); double time; int window[4];
    check(props->propGetDouble(in,kOfxPropTime,0,&time));
    check(props->propGetIntN(in,kOfxImageEffectPropRenderWindow,4,window));
    bool enabled=integer(i.enabled,time)!=0;
    const double whiteCode=number(i.whiteCode,time);
    double lo[3],hi[3],gain[3],shift[3],gamma[3];
    for(int c=0;c<3;c++){lo[c]=number(i.dmin[c],time);hi[c]=number(i.dmax[c],time);gain[c]=number(i.slope[c],time);shift[c]=number(i.shift[c],time);gamma[c]=number(i.gamma[c],time);}
    // Invalid/intermediate edits must not fail the OFX render or flood the host with dialogs.
    enabled=enabled&&revelare::validEndpoints(lo,hi,gain,shift,gamma);
    Image src(i.source,time),dst(i.output,time);
    for(int y=window[1];y<window[3];y++) {
        if(effects->abort(effect)) return kOfxStatOK;
        for(int x=window[0];x<window[2];x++) {
            auto* d=dst.pixel(x,y); auto* s=src.pixel(x,y); if(!d) continue;
            if(!s){std::fill(d,d+dst.channels,0.f);continue;}
            float alpha=src.channels==4?s[3]:1.f;
            for(int c=0;c<3;c++) {
                float v=src.premult ? (alpha>0?s[c]/alpha:0.f) : s[c];
                if(enabled) v=revelare::logChannel(v,lo[c],hi[c],gain[c],shift[c],gamma[c],whiteCode);
                d[c]=dst.premult?v*alpha:v;
            }
            if(dst.channels==4)d[3]=alpha;
        }
    }
    return kOfxStatOK;
}
void analyze(OfxImageEffectHandle effect,Instance& i,double time) {
    auto fn=loadCalibration();
    OfxRectD rod{}; check(effects->clipGetRegionOfDefinition(i.source,time,&rod));
    Image src(i.source,time,&rod);
    int w=src.bounds[2]-src.bounds[0],h=src.bounds[3]-src.bounds[1];
    if(w<20||h<20) throw std::runtime_error("Image is too small for analysis.");
    // Bounded analysis buffer, box averaged. The RAW decoder and files are never accessed.
    int step=std::max(1,(std::max(w,h)+1023)/1024);
    int aw=w/step,ah=h/step;
    std::vector<float> data(static_cast<size_t>(aw)*ah*3);
    for(int y=0;y<ah;y++) {
        if(effects->abort(effect)) throw std::runtime_error("Analysis cancelled; previous calibration retained.");
        for(int x=0;x<aw;x++) {
            double sum[3]={};
            for(int yy=0;yy<step;yy++)for(int xx=0;xx<step;xx++) {
                auto* s=src.pixel(src.bounds[0]+x*step+xx,src.bounds[1]+y*step+yy);
                float a=src.channels==4?s[3]:1.f;
                if(!std::isfinite(a)||a<0.999f) throw std::runtime_error("Analyze opaque scans before alpha/crop effects.");
                for(int c=0;c<3;c++) {
                    float v=src.premult?s[c]/a:s[c];
                    if(!std::isfinite(v)||v<0) throw std::runtime_error("Analysis needs finite, nonnegative linear input. Check upstream transforms.");
                    sum[c]+=v;
                }
            }
            for(int c=0;c<3;c++)data[(y*aw+x)*3+c]=static_cast<float>(sum[c]/(step*step));
        }
    }
    double r[4];for(int c=0;c<4;c++)r[c]=number(i.roi[c],time);
    if(r[0]<0||r[1]<0||r[2]<=0||r[3]<=0||r[0]+r[2]>1.000001||r[1]+r[3]>1.000001)
        throw std::runtime_error("ROI must fit inside [0,1].");
    int x=static_cast<int>(r[0]*aw),y=static_cast<int>(r[1]*ah);
    int rw=std::min(aw-x,static_cast<int>(r[2]*aw)),rh=std::min(ah-y,static_cast<int>(r[3]*ah));
    double oldMin[3],oldMax[3],oldSlope[3],oldShift[3],oldGamma[3];
    for(int c=0;c<3;c++){oldMin[c]=number(i.dmin[c],time);oldMax[c]=number(i.dmax[c],time);oldSlope[c]=number(i.slope[c],time);oldShift[c]=number(i.shift[c],time);oldGamma[c]=number(i.gamma[c],time);}
    int wasLocked=integer(i.lock,time),wasEnabled=integer(i.enabled,time);
    double result[8]{};char error[1024]{};
    // Analyze frame is an explicit request for a fresh automatic calibration.
    // The Dmin lock protects the numeric controls between analyses; it must not
    // silently turn this button into a Dmax-only operation.
    if(fn(data.data(),aw,ah,x,y,rw,rh,nullptr,result,error,sizeof(error)))
        throw std::runtime_error(error);
    if(effects->abort(effect))throw std::runtime_error("Analysis cancelled; previous calibration retained.");
    check(params->paramEditBegin(i.set,"Revelare calibration"));
    try {
        for(int c=0;c<3;c++) {
            check(params->paramSetValue(i.dmin[c],result[c]));
            check(params->paramSetValue(i.dmax[c],result[c+3]));
            check(params->paramSetValue(i.slope[c],1.0));
            check(params->paramSetValue(i.shift[c],0.0));
            check(params->paramSetValue(i.gamma[c],1.0));
        }
        check(params->paramSetValue(i.lock,1));check(params->paramSetValue(i.enabled,1));
    } catch(...) {
        for(int c=0;c<3;c++){params->paramSetValue(i.dmin[c],oldMin[c]);params->paramSetValue(i.dmax[c],oldMax[c]);params->paramSetValue(i.slope[c],oldSlope[c]);params->paramSetValue(i.shift[c],oldShift[c]);params->paramSetValue(i.gamma[c],oldGamma[c]);}
        params->paramSetValue(i.lock,wasLocked);params->paramSetValue(i.enabled,wasEnabled);
        params->paramEditEnd(i.set);throw;
    }
    check(params->paramEditEnd(i.set));updateLock(i,time);updateEndpointControls(i,time);
    int flags=static_cast<int>(std::llround(result[7]));
    int baseEvidence=(flags>>2)&3;
    const char* baseText=baseEvidence==1?"film-base mode":
        baseEvidence==2?"edge film base":baseEvidence==3?"content inference":"unknown base source";
    const char* highlightText=(flags&2)?"stable single-frame highlight":
        (flags&1)?"legacy highlight fallback":"roll highlight consensus";
    std::string status=std::string("Calibrated. Dmin locked. Dmin: ")+baseText+
        "; Dmax: "+highlightText+".";
    params->paramSetValue(i.status,status.c_str());
}
std::array<double,3> sampleDensityBox(OfxImageEffectHandle effect,Instance& i,double time,const char* label) {
    OfxRectD rod{};check(effects->clipGetRegionOfDefinition(i.source,time,&rod));
    Image src(i.source,time,&rod);
    int w=src.bounds[2]-src.bounds[0],h=src.bounds[3]-src.bounds[1];
    const auto position=point(i.samplePosition,time);
    const double rodWidth=rod.x2-rod.x1,rodHeight=rod.y2-rod.y1;
    if(!(rodWidth>0.0&&rodHeight>0.0))throw std::runtime_error("The source image has no usable region.");
    const double cx=(position[0]-rod.x1)/rodWidth,cy=(position[1]-rod.y1)/rodHeight;
    const double size=std::clamp(number(i.neutralSize,time),0.005,0.50);
    const double width=std::min(size,1.0),height=std::min(size,1.0);
    double r[4]={std::clamp(cx-width*.5,0.0,1.0-width),
                 std::clamp(cy-height*.5,0.0,1.0-height),width,height};
    if(cx<0.0||cx>1.0||cy<0.0||cy>1.0)
        throw std::runtime_error(std::string(label)+" sample position is outside the source image.");
    int x0=src.bounds[0]+static_cast<int>(r[0]*w),y0=src.bounds[1]+static_cast<int>(r[1]*h);
    int x1=std::min(src.bounds[2],x0+std::max(1,static_cast<int>(r[2]*w)));
    int y1=std::min(src.bounds[3],y0+std::max(1,static_cast<int>(r[3]*h)));
    if(x1-x0<2||y1-y0<2)throw std::runtime_error(std::string(label)+" ROI is too small.");
    std::array<std::vector<double>,3> samples;
    const size_t reserve=static_cast<size_t>(x1-x0)*static_cast<size_t>(y1-y0);
    for(auto& channel:samples)channel.reserve(reserve);
    for(int y=y0;y<y1;y++) {
      if(effects->abort(effect))throw std::runtime_error(std::string(label)+" sampling cancelled; previous calibration retained.");
      for(int x=x0;x<x1;x++) {
        auto* s=src.pixel(x,y);if(!s)continue;
        float a=src.channels==4?s[3]:1.f;
        if(!std::isfinite(a)||a<0.999f)throw std::runtime_error(std::string("Sample ")+label+" before alpha/crop effects.");
        for(int c=0;c<3;c++) {
            double v=src.premult?s[c]/a:s[c];
            if(!std::isfinite(v)||v<=0)throw std::runtime_error(std::string(label)+" patch contains invalid or non-positive linear values.");
            samples[c].push_back(-std::log10(std::max(v,1e-4)));
        }
      }
    }
    if(samples[0].empty())throw std::runtime_error(std::string(label)+" ROI contains no pixels.");
    std::array<double,3> density{};
    for(int c=0;c<3;c++) {
        auto& values=samples[c];const size_t mid=values.size()/2;
        std::nth_element(values.begin(),values.begin()+mid,values.end());
        density[c]=values[mid];
    }
    return density;
}
void sampleEndpoint(OfxImageEffectHandle effect,Instance& i,double time,bool filmBase) {
    const auto density=sampleDensityBox(effect,i,time,filmBase?"Dmin":"Dmax");
    double oldMin[3],oldMax[3],oldSlope[3],oldShift[3],oldGamma[3];
    for(int c=0;c<3;c++) {
        oldMin[c]=number(i.dmin[c],time);oldMax[c]=number(i.dmax[c],time);
        oldSlope[c]=number(i.slope[c],time);oldShift[c]=number(i.shift[c],time);oldGamma[c]=number(i.gamma[c],time);
        if(!filmBase && density[c]<=oldMin[c]+0.001)
            throw std::runtime_error("Dmax box is not denser than the locked Dmin in every channel.");
    }
    check(params->paramEditBegin(i.set,filmBase?"Sample film base":"Sample highlight endpoint"));
    try {
        for(int c=0;c<3;c++) {
            if(filmBase) {
                const double span=std::max(oldMax[c]-oldMin[c],0.10);
                check(params->paramSetValue(i.dmin[c],density[c]));
                check(params->paramSetValue(i.dmax[c],density[c]+span));
            } else check(params->paramSetValue(i.dmax[c],density[c]));
            check(params->paramSetValue(i.slope[c],1.0));
            check(params->paramSetValue(i.shift[c],0.0));
            check(params->paramSetValue(i.gamma[c],1.0));
        }
        check(params->paramSetValue(i.lock,1));check(params->paramSetValue(i.enabled,1));
        check(params->paramEditEnd(i.set));
    } catch(...) {
        for(int c=0;c<3;c++) {
            params->paramSetValue(i.dmin[c],oldMin[c]);params->paramSetValue(i.dmax[c],oldMax[c]);
            params->paramSetValue(i.slope[c],oldSlope[c]);params->paramSetValue(i.shift[c],oldShift[c]);params->paramSetValue(i.gamma[c],oldGamma[c]);
        }
        params->paramEditEnd(i.set);throw;
    }
    updateLock(i,time);updateEndpointControls(i,time);
    char text[320];std::snprintf(text,sizeof(text),
        filmBase?"Manual Dmin sampled from one co-sited box: R %.4f / G %.4f / B %.4f. Previous spans retained until Dmax is sampled."
                :"Manual Dmax sampled from one co-sited box: R %.4f / G %.4f / B %.4f. Dmin remains locked.",
        density[0],density[1],density[2]);
    check(params->paramSetValue(i.status,text));
}
void sampleNeutral(OfxImageEffectHandle effect,Instance& i,double time) {
    if(!integer(i.enabled,time)) throw std::runtime_error("Calibrate and enable inversion before sampling neutral grey.");
    const auto density=sampleDensityBox(effect,i,time,"Neutral");
    double target=number(i.neutralTarget,time),before[3],solved[3],oldGamma[3];
    const double codeSpan=number(i.whiteCode,time)-95.0;
    if(!(codeSpan>0.0))throw std::runtime_error("Cineon white placement must exceed code 95.");
    for(int c=0;c<3;c++) {
        double lo=number(i.dmin[c],time),hi=number(i.dmax[c],time);
        double slope=number(i.slope[c],time),shift=number(i.shift[c],time);
        double position=(density[c]-lo)/(hi-lo);
        before[c]=95.0+shift+codeSpan*slope*revelare::midtoneCurve(position,number(i.gamma[c],time));
        oldGamma[c]=number(i.gamma[c],time);
    }
    const bool absolute=integer(i.neutralAbsolute,time)!=0;
    if(!absolute) target=(before[0]+before[1]+before[2])/3.0;
    for(int c=0;c<3;c++) {
        double lo=number(i.dmin[c],time),hi=number(i.dmax[c],time);
        double slope=number(i.slope[c],time),shift=number(i.shift[c],time);
        double position=(density[c]-lo)/(hi-lo);
        double desired=(target-95.0-shift)/(codeSpan*slope);
        if(!(position>0.0&&position<1.0)||!(desired>0.0&&desired<1.0))
            throw std::runtime_error("Neutral patch must lie strictly between the calibrated Dmin and Dmax in every channel.");
        solved[c]=std::log(desired)/std::log(position);
        if(!std::isfinite(solved[c])||solved[c]<0.20||solved[c]>5.0)
            throw std::runtime_error("Neutral correction is outside the safe gamma range. Check the patch and endpoints.");
    }
    check(params->paramEditBegin(i.set,"Neutral grey calibration"));
    try {
        for(int c=0;c<3;c++)check(params->paramSetValue(i.gamma[c],solved[c]));
        check(params->paramEditEnd(i.set));
    } catch(...) {
        for(int c=0;c<3;c++)params->paramSetValue(i.gamma[c],oldGamma[c]);
        params->paramEditEnd(i.set);throw;
    }
    char text[512];std::snprintf(text,sizeof(text),
        "Current sample R %.1f / G %.1f / B %.1f -> %s %.1f; midtone gamma R %.4f / G %.4f / B %.4f. Dmin/Dmax unchanged; white code %.0f.",
        before[0],before[1],before[2],absolute?"card target":"preserved level",target,solved[0],solved[1],solved[2],number(i.whiteCode,time));
    check(params->paramSetValue(i.neutralStatus,text));
    check(params->paramSetValue(i.status,"Neutral correction applied. The yellow marker is the sampled area; Dmin and Dmax remain locked."));
}

bool interactContext(OfxPropertySetHandle in,OfxImageEffectHandle& effect,Instance*& i,double& time) {
    void* raw=nullptr;
    if(!in || props->propGetPointer(in,kOfxPropEffectInstance,0,&raw)!=kOfxStatOK || !raw)return false;
    effect=reinterpret_cast<OfxImageEffectHandle>(raw);
    try { i=instance(effect); }
    catch(...) { return false; }
    time=0.0; props->propGetDouble(in,kOfxPropTime,0,&time);
    return true;
}
OfxStatus overlayEntry(const char* action,const void* handle,OfxPropertySetHandle in,OfxPropertySetHandle) {
    try {
        if(std::strcmp(action,kOfxActionDescribeInteract)==0) {
            auto descriptor=reinterpret_cast<OfxPropertySetHandle>(const_cast<void*>(handle));
            check(props->propSetString(descriptor,kOfxInteractPropSlaveToParam,0,"neutralOverlay"));
            check(props->propSetString(descriptor,kOfxInteractPropSlaveToParam,1,"neutralSize"));
            check(props->propSetString(descriptor,kOfxInteractPropSlaveToParam,2,"samplePosition"));
            return kOfxStatOK;
        }
        if(std::strcmp(action,kOfxActionCreateInstanceInteract)==0 || std::strcmp(action,kOfxActionDestroyInstanceInteract)==0)
            return kOfxStatOK;
        OfxImageEffectHandle effect{};Instance* i=nullptr;double time=0.0;
        if(!interactContext(in,effect,i,time) || !integer(i->neutralOverlay,time))return kOfxStatReplyDefault;
        if(std::strcmp(action,kOfxInteractActionDraw)==0) {
            OfxRectD rod{};if(effects->clipGetRegionOfDefinition(i->source,time,&rod)!=kOfxStatOK)return kOfxStatReplyDefault;
            const auto position=point(i->samplePosition,time);
            const double size=std::clamp(number(i->neutralSize,time),0.005,0.5);
            const double hw=(rod.x2-rod.x1)*size*.5,hh=(rod.y2-rod.y1)*size*.5;
            const double x0=position[0]-hw,x1=position[0]+hw;
            const double y0=position[1]-hh,y1=position[1]+hh;
            double pixelScale[2]={1.0,1.0};props->propGetDoubleN(in,kOfxInteractPropPixelScale,2,pixelScale);
            const double cx=(x0+x1)*.5,cy=(y0+y1)*.5;
            const double hx=std::max((x1-x0)*.5,9.0*std::abs(pixelScale[0]));
            const double hy=std::max((y1-y0)*.5,9.0*std::abs(pixelScale[1]));
            glPushAttrib(GL_CURRENT_BIT|GL_ENABLE_BIT|GL_LINE_BIT);
            glDisable(GL_DEPTH_TEST);glEnable(GL_BLEND);glBlendFunc(GL_SRC_ALPHA,GL_ONE_MINUS_SRC_ALPHA);
            glColor4f(1.f,.78f,.08f,.15f);glBegin(GL_QUADS);glVertex2d(x0,y0);glVertex2d(x1,y0);glVertex2d(x1,y1);glVertex2d(x0,y1);glEnd();
            glLineWidth(2.f);glColor4f(1.f,.82f,.12f,1.f);glBegin(GL_LINE_LOOP);glVertex2d(x0,y0);glVertex2d(x1,y0);glVertex2d(x1,y1);glVertex2d(x0,y1);glEnd();
            glBegin(GL_LINES);glVertex2d(cx-hx,cy);glVertex2d(cx+hx,cy);glVertex2d(cx,cy-hy);glVertex2d(cx,cy+hy);glEnd();
            glPopAttrib();return kOfxStatOK;
        }
        // Resolve's native XY handle owns pointer input. Returning default here
        // lets the host move it with the exact viewer transform it is displaying.
        return kOfxStatReplyDefault;
    } catch(...) { return kOfxStatReplyDefault; }
}
OfxStatus changed(OfxImageEffectHandle effect,OfxPropertySetHandle in) {
    auto& i=*instance(effect);
    const bool userEdited=stringProp(in,kOfxPropChangeReason)==kOfxChangeUserEdited;
    auto name=stringProp(in,kOfxPropName);double time;check(props->propGetDouble(in,kOfxPropTime,0,&time));
    if(name=="lockBase"){updateLock(i,time);return kOfxStatOK;}
    if(name=="neutralSize" || name=="samplePosition") return kOfxStatOK;
    if(name=="resetTrim" && userEdited) {
        check(params->paramEditBegin(i.set,"Reset gain and shift"));
        for(auto p:i.slope)params->paramSetValue(p,1.0);
        for(auto p:i.shift)params->paramSetValue(p,0.0);
        for(auto p:i.gamma)params->paramSetValue(p,1.0);
        check(params->paramEditEnd(i.set));return kOfxStatOK;
    }
    if((name=="diffuseWhite" || name=="trueDmax") && userEdited) {
        const double target=name=="diffuseWhite" ? 685.0 : 1000.0;
        check(params->paramEditBegin(i.set,name=="diffuseWhite" ? "Set diffuse-white placement" : "Set true Dmax placement"));
        check(params->paramSetValue(i.whiteCode,target));
        check(params->paramEditEnd(i.set));
        validateStatus(i,time);return kOfxStatOK;
    }
    if(name=="analyze" && userEdited) {
        try {analyze(effect,i,time);}
        catch(const std::exception& e){params->paramSetValue(i.status,e.what());}
        // A rejected analysis leaves the existing grade intact; it is not a render failure.
        return kOfxStatOK;
    }
    if((name=="sampleDmin" || name=="sampleDmax") && userEdited) {
        try {sampleEndpoint(effect,i,time,name=="sampleDmin");}
        catch(const std::exception& e){params->paramSetValue(i.status,e.what());}
        return kOfxStatOK;
    }
    if(name=="sampleNeutral" && userEdited) {
        try {sampleNeutral(effect,i,time);}
        catch(const std::exception& e){params->paramSetValue(i.neutralStatus,e.what());}
        return kOfxStatOK;
    }
    for(int c=0;c<3;c++) {
        if(name==minNames[c]) {updateEndpointControls(i,time);validateStatus(i,time);return kOfxStatOK;}
        if(name==maxNames[c] || name==gainNames[c] || name==shiftNames[c] || name==gammaNames[c] || name=="enabled" || name=="whiteCode") {
            validateStatus(i,time);return kOfxStatOK;
        }
    }
    return kOfxStatReplyDefault;
}
OfxStatus entry(const char* action,const void* handle,OfxPropertySetHandle in,OfxPropertySetHandle) {
    auto effect=reinterpret_cast<OfxImageEffectHandle>(const_cast<void*>(handle));
    try {
        if(std::strcmp(action,kOfxActionLoad)==0) {
            props=static_cast<const OfxPropertySuiteV1*>(host->fetchSuite(host->host,kOfxPropertySuite,1));
            effects=static_cast<const OfxImageEffectSuiteV1*>(host->fetchSuite(host->host,kOfxImageEffectSuite,1));
            params=static_cast<const OfxParameterSuiteV1*>(host->fetchSuite(host->host,kOfxParameterSuite,1));
            messages=static_cast<const OfxMessageSuiteV1*>(host->fetchSuite(host->host,kOfxMessageSuite,1));
            interacts=static_cast<const OfxInteractSuiteV1*>(host->fetchSuite(host->host,kOfxInteractSuite,1));
            return props&&effects&&params?kOfxStatOK:kOfxStatErrMissingHostFeature;
        }
        if(std::strcmp(action,kOfxActionDescribe)==0)return describe(effect);
        if(std::strcmp(action,kOfxImageEffectActionDescribeInContext)==0)return describeContext(effect);
        if(std::strcmp(action,kOfxActionCreateInstance)==0)return create(effect);
        if(std::strcmp(action,kOfxActionDestroyInstance)==0){delete instance(effect);return kOfxStatOK;}
        if(std::strcmp(action,kOfxImageEffectActionRender)==0)return render(effect,in);
        if(std::strcmp(action,kOfxActionInstanceChanged)==0)return changed(effect,in);
        return kOfxStatReplyDefault;
    } catch(const std::bad_alloc&) {return kOfxStatErrMemory;}
      catch(const std::exception& e) {notify(effect,e.what());return kOfxStatFailed;}
      catch(...) {return kOfxStatFailed;}
}
void setHost(OfxHost* h){host=h;}
OfxPlugin plugin={kOfxImageEffectPluginApi,1,"org.openrevelare.negative.prototype",0,3,setHost,entry};
}
extern "C" __attribute__((visibility("default"))) int OfxGetNumberOfPlugins(){return 1;}
extern "C" __attribute__((visibility("default"))) OfxPlugin* OfxGetPlugin(int index){return index==0?&plugin:nullptr;}
