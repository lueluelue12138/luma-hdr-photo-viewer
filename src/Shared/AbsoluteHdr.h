#pragma once
#include <cstdint>
#include <cstring>
#include <span>
#include <vector>

namespace luma::absolute_hdr {
// Optional authoring metadata, not a replacement for the standard gain map.
// Only this exact ImageDescription declares absolute 203-nit reconstruction.
// Generic Android readers ignore the description and use XMP / ISO adaptation.
inline constexpr char description[]="Luma Absolute HDR v1; ReferenceWhiteNits=203; Source=scRGB80";
inline std::vector<uint8_t> exif(){
    // Exif\0\0 + little-endian TIFF + one ASCII ImageDescription entry.
    std::vector<uint8_t> out(6+26+sizeof(description),0);
    const uint8_t header[]={ 'E','x','i','f',0,0,'I','I',42,0,8,0,0,0,1,0,14,1,2,0 };
    std::memcpy(out.data(),header,sizeof(header));
    uint32_t count=sizeof(description),offset=26;
    std::memcpy(out.data()+20,&count,4);std::memcpy(out.data()+24,&offset,4);
    std::memcpy(out.data()+32,description,sizeof(description));return out;
}
inline bool contains(std::span<const uint8_t> bytes){
    if(bytes.size()>=6&&std::memcmp(bytes.data(),"Exif\0\0",6)==0)bytes=bytes.subspan(6);
    if(bytes.size()<8||bytes[0]!='I'||bytes[1]!='I'||bytes[2]!=42||bytes[3]!=0)return false;
    auto u16=[&](size_t p){return uint16_t(bytes[p])|(uint16_t(bytes[p+1])<<8);};
    auto u32=[&](size_t p){return uint32_t(bytes[p])|(uint32_t(bytes[p+1])<<8)|(uint32_t(bytes[p+2])<<16)|(uint32_t(bytes[p+3])<<24);};
    size_t ifd=u32(4);if(ifd>bytes.size()-2)return false;size_t count=u16(ifd);
    if(count>(bytes.size()-ifd-2)/12)return false;
    for(size_t n=0;n<count;n++){size_t p=ifd+2+n*12;
        if(u16(p)==270&&u16(p+2)==2&&u32(p+4)==sizeof(description)){
            size_t offset=u32(p+8);if(offset>bytes.size()||sizeof(description)>bytes.size()-offset)return false;
            return std::memcmp(bytes.data()+offset,description,sizeof(description))==0;
        }
    }return false;
}
}
