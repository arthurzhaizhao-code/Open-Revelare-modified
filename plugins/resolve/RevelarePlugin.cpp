#include "ofxImageEffect.h"
#include "ofxProperty.h"
#include "ofxParam.h"
#include "ofxMessage.h"
#include "CalibrationAbi.h"
#include "LogTransform.h"
#include <array>
#include <cstring>
#include <dlfcn.h>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
OfxHost* host = nullptr;
const OfxPropertySuiteV1* props = nullptr;
const OfxImageEffectSuiteV1* effects = nullptr;
const OfxParameterSuiteV1* params = nullptr;
const OfxMessageSuiteV1* messages = nullptr;
const char* minNames[] = {"dminR", "dminG", "dminB"};
const char* maxNames[] = {"dmaxR", "dmaxG", "dmaxB"};
const char* gainNames[] = {"slopeR", "slopeG", "slopeB"};
const char* shiftNames[] = {"shiftR", "shiftG", "shiftB"};
const char* roiNames[] = {"roiLeft", "roiBottom", "roiWidth", "roiHeight"};
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
    OfxParamHandle enabled{}, lock{}, status{};
    std::array<OfxParamHandle, 3> dmin{}, dmax{}, slope{}, shift{};
    std::array<OfxParamHandle, 4> roi{};
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
    double lo[3],hi[3],gain[3],shift[3];
    for(int c=0;c<3;c++){lo[c]=number(i.dmin[c],time);hi[c]=number(i.dmax[c],time);gain[c]=number(i.slope[c],time);shift[c]=number(i.shift[c],time);}
    if(!revelare::validEndpoints(lo,hi,gain,shift))
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
void defineNumber(OfxParamSetHandle set,const char* name,const char* label,double value,double lo,double hi,
                  double increment=0.001,int digits=4) {
    auto p=define(set,kOfxParamTypeDouble,name,label);
    check(props->propSetDouble(p,kOfxParamPropDefault,0,value));
    check(props->propSetDouble(p,kOfxParamPropMin,0,lo));
    check(props->propSetDouble(p,kOfxParamPropMax,0,hi));
    check(props->propSetDouble(p,kOfxParamPropDisplayMin,0,lo));
    check(props->propSetDouble(p,kOfxParamPropDisplayMax,0,hi));
    props->propSetDouble(p,kOfxParamPropIncrement,0,increment);
    props->propSetInt(p,kOfxParamPropDigits,0,digits);
}
OfxStatus describe(OfxImageEffectHandle effect) {
    OfxPropertySetHandle p{}; check(effects->getPropertySet(effect,&p));
    check(props->propSetString(p,kOfxPropLabel,0,"Revelare Negative (Prototype)"));
    check(props->propSetString(p,kOfxImageEffectPluginPropGrouping,0,"Film Negative"));
    check(props->propSetString(p,kOfxImageEffectPropSupportedContexts,0,kOfxImageEffectContextFilter));
    check(props->propSetString(p,kOfxImageEffectPropSupportedPixelDepths,0,kOfxBitDepthFloat));
    check(props->propSetInt(p,kOfxImageEffectPropSupportsTiles,0,0));
    check(props->propSetInt(p,kOfxImageEffectPropTemporalClipAccess,0,0));
    check(props->propSetString(p,kOfxImageEffectPluginRenderThreadSafety,0,kOfxImageEffectRenderFullySafe));
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
    p=define(set,kOfxParamTypeBoolean,"lockBase","锁定 Dmin / Lock film base");
    check(props->propSetInt(p,kOfxParamPropDefault,0,0));
    for(int c=0;c<3;c++) {
        defineNumber(set,minNames[c],minNames[c],0.0,-4.0,4.0);
        defineNumber(set,maxNames[c],maxNames[c],2.0,-3.9,8.0);
        defineNumber(set,gainNames[c],gainNames[c],1.0,0.25,4.0);
        defineNumber(set,shiftNames[c],shiftNames[c],0.0,-256.0,256.0,1.0,1);
    }
    define(set,kOfxParamTypePushButton,"resetTrim","重置通道调整 / Reset gain + shift");
    const double roiDefault[]={0.05,0.05,0.90,0.90};
    for(int c=0;c<4;c++) defineNumber(set,roiNames[c],roiNames[c],roiDefault[c],0.0,1.0);
    p=define(set,kOfxParamTypeString,"analysisStatus","标定状态 / Analysis status");
    check(props->propSetString(p,kOfxParamPropDefault,0,"Not calibrated. Linear input required; primaries are preserved."));
    props->propSetInt(p,kOfxParamPropEnabled,0,0);
    return kOfxStatOK;
}
OfxStatus create(OfxImageEffectHandle effect) {
    auto i=std::make_unique<Instance>();
    check(effects->clipGetHandle(effect,kOfxImageEffectSimpleSourceClipName,&i->source,nullptr));
    check(effects->clipGetHandle(effect,kOfxImageEffectOutputClipName,&i->output,nullptr));
    check(effects->getParamSet(effect,&i->set));
    i->enabled=parameter(i->set,"enabled"); i->lock=parameter(i->set,"lockBase"); i->status=parameter(i->set,"analysisStatus");
    for(int c=0;c<3;c++) {i->dmin[c]=parameter(i->set,minNames[c]);i->dmax[c]=parameter(i->set,maxNames[c]);i->slope[c]=parameter(i->set,gainNames[c]);i->shift[c]=parameter(i->set,shiftNames[c]);}
    for(int c=0;c<4;c++) i->roi[c]=parameter(i->set,roiNames[c]);
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
    double lo[3],hi[3],gain[3],shift[3];
    for(int c=0;c<3;c++){lo[c]=number(i.dmin[c],time);hi[c]=number(i.dmax[c],time);gain[c]=number(i.slope[c],time);shift[c]=number(i.shift[c],time);}
    // Invalid/intermediate edits must not fail the OFX render or flood the host with dialogs.
    enabled=enabled&&revelare::validEndpoints(lo,hi,gain,shift);
    Image src(i.source,time),dst(i.output,time);
    for(int y=window[1];y<window[3];y++) {
        if(effects->abort(effect)) return kOfxStatOK;
        for(int x=window[0];x<window[2];x++) {
            auto* d=dst.pixel(x,y); auto* s=src.pixel(x,y); if(!d) continue;
            if(!s){std::fill(d,d+dst.channels,0.f);continue;}
            float alpha=src.channels==4?s[3]:1.f;
            for(int c=0;c<3;c++) {
                float v=src.premult ? (alpha>0?s[c]/alpha:0.f) : s[c];
                if(enabled) v=revelare::logChannel(v,lo[c],hi[c],gain[c],shift[c]);
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
    double oldMin[3],oldMax[3],oldSlope[3],oldShift[3];
    for(int c=0;c<3;c++){oldMin[c]=number(i.dmin[c],time);oldMax[c]=number(i.dmax[c],time);oldSlope[c]=number(i.slope[c],time);oldShift[c]=number(i.shift[c],time);}
    int wasLocked=integer(i.lock,time),wasEnabled=integer(i.enabled,time);
    double result[8]{};char error[1024]{};
    if(fn(data.data(),aw,ah,x,y,rw,rh,wasLocked?oldMin:nullptr,result,error,sizeof(error)))
        throw std::runtime_error(error);
    if(effects->abort(effect))throw std::runtime_error("Analysis cancelled; previous calibration retained.");
    check(params->paramEditBegin(i.set,"Revelare calibration"));
    try {
        for(int c=0;c<3;c++) {
            if(!wasLocked)check(params->paramSetValue(i.dmin[c],result[c]));
            check(params->paramSetValue(i.dmax[c],result[c+3]));
            check(params->paramSetValue(i.slope[c],1.0));
            check(params->paramSetValue(i.shift[c],0.0));
        }
        check(params->paramSetValue(i.lock,1));check(params->paramSetValue(i.enabled,1));
    } catch(...) {
        for(int c=0;c<3;c++){params->paramSetValue(i.dmin[c],oldMin[c]);params->paramSetValue(i.dmax[c],oldMax[c]);params->paramSetValue(i.slope[c],oldSlope[c]);params->paramSetValue(i.shift[c],oldShift[c]);}
        params->paramSetValue(i.lock,wasLocked);params->paramSetValue(i.enabled,wasEnabled);
        params->paramEditEnd(i.set);throw;
    }
    check(params->paramEditEnd(i.set));updateLock(i,time);updateEndpointControls(i,time);
    std::string status=result[7]?"Calibrated (fallback). Dmin locked; inspect Log scopes.":
        "Calibrated. Dmin locked. Highlight confidence: "+std::to_string(result[6]);
    params->paramSetValue(i.status,status.c_str());
}
OfxStatus changed(OfxImageEffectHandle effect,OfxPropertySetHandle in) {
    auto& i=*instance(effect);
    const bool userEdited=stringProp(in,kOfxPropChangeReason)==kOfxChangeUserEdited;
    auto name=stringProp(in,kOfxPropName);double time;check(props->propGetDouble(in,kOfxPropTime,0,&time));
    if(name=="lockBase"){updateLock(i,time);return kOfxStatOK;}
    if(name=="resetTrim" && userEdited) {
        check(params->paramEditBegin(i.set,"Reset gain and shift"));
        for(auto p:i.slope)params->paramSetValue(p,1.0);
        for(auto p:i.shift)params->paramSetValue(p,0.0);
        check(params->paramEditEnd(i.set));return kOfxStatOK;
    }
    if(name=="analyze" && userEdited) {
        try {analyze(effect,i,time);}
        catch(const std::exception& e){params->paramSetValue(i.status,e.what());}
        // A rejected analysis leaves the existing grade intact; it is not a render failure.
        return kOfxStatOK;
    }
    for(int c=0;c<3;c++) {
        if(name==minNames[c]) {updateEndpointControls(i,time);validateStatus(i,time);return kOfxStatOK;}
        if(name==maxNames[c] || name==gainNames[c] || name==shiftNames[c] || name=="enabled") {
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
OfxPlugin plugin={kOfxImageEffectPluginApi,1,"org.openrevelare.negative.prototype",0,2,setHost,entry};
}
extern "C" __attribute__((visibility("default"))) int OfxGetNumberOfPlugins(){return 1;}
extern "C" __attribute__((visibility("default"))) OfxPlugin* OfxGetPlugin(int index){return index==0?&plugin:nullptr;}
