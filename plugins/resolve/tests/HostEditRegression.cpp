// Exercise the real OFX action handlers with a minimal CPU host, including the
// invalid endpoint edit that caused repeated Resolve Open FX Error dialogs.
#include "../RevelarePlugin.cpp"
#include <cassert>
#include <cstdarg>
#include <iostream>

struct Value { double number=0; int integer=0; std::string text; };
static Instance state;
static float inputPixel[4]={.5f,.4f,.3f,1.f}, outputPixel[4]={};
static int inputTag,outputTag,renderTag,effectTag;
static std::string changedName="dmaxR";
static int errors=0;
static OfxStatus getValue(OfxParamHandle h,double time,...) {
    va_list a;va_start(a,time);
    auto* v=reinterpret_cast<Value*>(h);
    if(h==state.enabled || h==state.lock) *va_arg(a,int*)=v->integer;
    else *va_arg(a,double*)=v->number;
    va_end(a);return kOfxStatOK;
}
static OfxStatus setValue(OfxParamHandle h,...) {
    va_list a;va_start(a,h);
    assert(h==state.status);
    reinterpret_cast<Value*>(h)->text=va_arg(a,const char*);
    va_end(a);return kOfxStatOK;
}
static OfxStatus hostMessage(void*,const char*,const char*,const char*,...) {errors++;return kOfxStatOK;}
int main() {
    OfxPropertySuiteV1 property{};OfxImageEffectSuiteV1 effect{};OfxParameterSuiteV1 parameterSuite{};OfxMessageSuiteV1 message{};
    props=&property;effects=&effect;params=&parameterSuite;messages=&message;
    property.propGetPointer=[](OfxPropertySetHandle p,const char* name,int,void** v)->OfxStatus {
        if(std::strcmp(name,kOfxPropInstanceData)==0)*v=&state;
        else *v=p==reinterpret_cast<OfxPropertySetHandle>(&inputTag)?inputPixel:outputPixel;
        return kOfxStatOK;
    };
    property.propGetDouble=[](OfxPropertySetHandle,const char*,int,double* v)->OfxStatus{*v=0;return kOfxStatOK;};
    property.propGetIntN=[](OfxPropertySetHandle,const char*,int,int* v)->OfxStatus{v[0]=v[1]=0;v[2]=v[3]=1;return kOfxStatOK;};
    property.propGetInt=[](OfxPropertySetHandle,const char*,int,int* v)->OfxStatus{*v=16;return kOfxStatOK;};
    property.propGetString=[](OfxPropertySetHandle,const char* name,int,char** v)->OfxStatus {
        const char* s=kOfxImageUnPreMultiplied;
        if(std::strcmp(name,kOfxImageEffectPropPixelDepth)==0)s=kOfxBitDepthFloat;
        if(std::strcmp(name,kOfxImageEffectPropComponents)==0)s=kOfxImageComponentRGBA;
        if(std::strcmp(name,kOfxPropChangeReason)==0)s=kOfxChangeUserEdited;
        if(std::strcmp(name,kOfxPropName)==0)s=changedName.c_str();
        *v=const_cast<char*>(s);return kOfxStatOK;
    };
    effect.getPropertySet=[](OfxImageEffectHandle,OfxPropertySetHandle* p)->OfxStatus{*p=reinterpret_cast<OfxPropertySetHandle>(&effectTag);return kOfxStatOK;};
    effect.clipGetImage=[](OfxImageClipHandle clip,double,const OfxRectD*,OfxPropertySetHandle* p)->OfxStatus{*p=reinterpret_cast<OfxPropertySetHandle>(clip);return kOfxStatOK;};
    effect.clipReleaseImage=[](OfxPropertySetHandle)->OfxStatus{return kOfxStatOK;};
    effect.abort=[](OfxImageEffectHandle)->int{return 0;};
    parameterSuite.paramGetValueAtTime=getValue;parameterSuite.paramSetValue=setValue;
    message.message=hostMessage;
    Value enabled,locked,status,neutralStatus,target,lo[3],hi[3],gain[3],shift[3],gamma[3];enabled.integer=1;target.number=470;
    state.enabled=reinterpret_cast<OfxParamHandle>(&enabled);state.lock=reinterpret_cast<OfxParamHandle>(&locked);state.status=reinterpret_cast<OfxParamHandle>(&status);
    state.neutralStatus=reinterpret_cast<OfxParamHandle>(&neutralStatus);state.neutralTarget=reinterpret_cast<OfxParamHandle>(&target);
    state.source=reinterpret_cast<OfxImageClipHandle>(&inputTag);state.output=reinterpret_cast<OfxImageClipHandle>(&outputTag);
    for(int c=0;c<3;c++){
        lo[c].number=.1427703952;hi[c].number=.1907345347;gain[c].number=1;gamma[c].number=1;
        state.dmin[c]=reinterpret_cast<OfxParamHandle>(&lo[c]);state.dmax[c]=reinterpret_cast<OfxParamHandle>(&hi[c]);state.slope[c]=reinterpret_cast<OfxParamHandle>(&gain[c]);state.shift[c]=reinterpret_cast<OfxParamHandle>(&shift[c]);state.gamma[c]=reinterpret_cast<OfxParamHandle>(&gamma[c]);
    }
    auto handle=reinterpret_cast<OfxImageEffectHandle>(&effectTag);auto args=reinterpret_cast<OfxPropertySetHandle>(&renderTag);
    assert(entry(kOfxImageEffectActionRender,handle,args,nullptr)==kOfxStatOK);
    assert(outputPixel[0]!=inputPixel[0]);
    // Drag across the calibrated Dmin, including equality. Every render succeeds.
    for(double v:{.1427703952,.1,-1.}) {
        hi[0].number=v;
        assert(entry(kOfxActionInstanceChanged,handle,args,nullptr)==kOfxStatOK);
        assert(status.text.find("bypassed")!=std::string::npos);
        for(int repeat=0;repeat<20;repeat++)assert(entry(kOfxImageEffectActionRender,handle,args,nullptr)==kOfxStatOK);
        for(int c=0;c<4;c++)assert(outputPixel[c]==inputPixel[c]);
        assert(lo[0].number==.1427703952); // no hidden change to film base
    }
    hi[0].number=.2;
    assert(entry(kOfxImageEffectActionRender,handle,args,nullptr)==kOfxStatOK);
    assert(outputPixel[0]!=inputPixel[0]);assert(errors==0);
    std::cout<<"OFX invalid endpoint edit, bypass, recovery and no error dialogs passed\n";
}
