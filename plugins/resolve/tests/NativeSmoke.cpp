#include "../CalibrationAbi.h"
#include "../LogTransform.h"
#include "ofxImageEffect.h"
#include <cassert>
#include <cstring>
#include <dlfcn.h>
#include <iostream>
#include <random>
#include <vector>

int main(int argc, char** argv) {
    // The mathematical contract is independent of OFX or the analysis runtime.
    double lo[3]={.09,.29,.54},hi[3]={1.8,2.1,2.4},gain[3]={1,1,1};
    assert(revelare::validEndpoints(lo,hi,gain));
    for(int c=0;c<3;c++) {
        float base=static_cast<float>(std::pow(10.,-lo[c]));
        float top=static_cast<float>(std::pow(10.,-hi[c]));
        assert(std::abs(revelare::logChannel(base,lo[c],hi[c],1)-95.f/1023)<1e-6);
        assert(std::abs(revelare::logChannel(base,lo[c],hi[c],2)-95.f/1023)<1e-6);
        assert(std::abs(revelare::logChannel(top,lo[c],hi[c],1)-1032.f/1023)<1e-6);
        assert(revelare::logChannel(top,lo[c],hi[c],1)>1); // preserve superwhite
    }
    float transmission[3]={.02f,.03f,.04f},before[3],after[3];
    for(int c=0;c<3;c++)before[c]=revelare::logChannel(transmission[c],lo[c],hi[c],gain[c]);
    gain[0]=1.2;
    for(int c=0;c<3;c++)after[c]=revelare::logChannel(transmission[c],lo[c],hi[c],gain[c]);
    assert(before[0]!=after[0]);assert(before[1]==after[1]);assert(before[2]==after[2]);
    const float shifted=revelare::logChannel(transmission[1],lo[1],hi[1],gain[1],10.0);
    assert(std::abs((shifted-after[1])-10.f/1023.f)<1e-6);
    gain[0]=0;assert(!revelare::validEndpoints(lo,hi,gain));
    if(argc==1){std::cout<<"Log transform tests passed\n";return 0;}
    assert(argc==3);
    void* lib=dlopen(argv[1],RTLD_NOW|RTLD_LOCAL);
    if(!lib){std::cerr<<dlerror()<<'\n';return 1;}
    auto abi=reinterpret_cast<CalibrationAbi>(dlsym(lib,"or_calibration_abi"));
    auto analyze=reinterpret_cast<AnalyzeSingle>(dlsym(lib,"or_analyze_single"));
    assert(abi&&abi()==1&&analyze);
    std::vector<float> rgb(100*100*3);
    std::mt19937 rng(42);std::uniform_real_distribution<double> dist(.2,1.6);
    for(int p=0;p<10000;p++) {
        double d=dist(rng);
        for(int c=0;c<3;c++)rgb[p*3+c]=static_cast<float>(std::pow(10.,-(d+c*.12)));
    }
    auto unchanged=rgb;
    double result[8]={};char error[1024]={};
    assert(analyze(rgb.data(),100,100,5,5,90,90,lo,result,error,sizeof(error))==0);
    for(int c=0;c<3;c++){assert(result[c]==lo[c]);assert(result[c+3]>result[c]);}
    assert(rgb==unchanged);
    assert(analyze(rgb.data(),100,100,5,5,90,90,nullptr,result,error,sizeof(error))==0);
    for(int c=0;c<3;c++)assert(std::isfinite(result[c])&&result[c+3]>result[c]);
    std::fill(rgb.begin(),rgb.end(),0.f);
    std::fill(result,result+8,-99.);
    assert(analyze(rgb.data(),100,100,0,0,100,100,lo,result,error,sizeof(error))!=0);
    for(double v:result)assert(v==-99); // failure cannot partially overwrite the candidate
    assert(error[0]);
    assert(analyze(nullptr,100,100,0,0,100,100,nullptr,result,error,sizeof(error))!=0);
    assert(analyze(rgb.data(),100,100,99,0,90,90,nullptr,result,error,sizeof(error))!=0);
    void* ofx=dlopen(argv[2],RTLD_NOW|RTLD_LOCAL);
    if(!ofx){std::cerr<<dlerror()<<'\n';return 1;}
    auto count=reinterpret_cast<int(*)()>(dlsym(ofx,"OfxGetNumberOfPlugins"));
    auto get=reinterpret_cast<OfxPlugin*(*)(int)>(dlsym(ofx,"OfxGetPlugin"));
    assert(count&&get&&count()==1&&get(0)&&!get(1));
    assert(std::strcmp(get(0)->pluginApi,kOfxImageEffectPluginApi)==0);
    assert(std::strcmp(get(0)->pluginIdentifier,"org.openrevelare.negative.prototype")==0);
    std::cout<<"Log transform, NativeAOT ABI, analysis, failure isolation and OFX exports passed\n";
    // NativeAOT runtime is process lifetime. Do not dlclose it.
}
