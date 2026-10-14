#pragma once
// This product includes Gain Map technology under license by Adobe.
// Adobe/Android XMP permits scalar properties and ordered one/three-value
// arrays. The pinned codec's XMP parser reads only scalar attributes.
uhdr_gainmap_metadata_t readGainXmp(const std::vector<uint8_t>& packet){
    constexpr wchar_t hdrNs[]=L"http://ns.adobe.com/hdr-gain-map/1.0/";
    constexpr wchar_t rdfNs[]=L"http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    std::string prefix="http://ns.adobe.com/xap/1.0/";prefix.push_back(0);
    size_t start=packet.size()>=prefix.size()&&memcmp(packet.data(),prefix.data(),prefix.size())==0?prefix.size():0;
    if(packet.size()<=start||packet.size()-start>1024*1024)throw Failure{WINCODEC_ERR_BADIMAGE};
    ComPtr<IStream> stream;stream.Attach(SHCreateMemStream(packet.data()+start,UINT(packet.size()-start)));if(!stream)throw std::bad_alloc();
    ComPtr<IXmlReader> reader;check(CreateXmlReader(__uuidof(IXmlReader),reinterpret_cast<void**>(reader.GetAddressOf()),nullptr));
    check(reader->SetProperty(XmlReaderProperty_DtdProcessing,DtdProcessing_Prohibit));check(reader->SetProperty(XmlReaderProperty_MaxElementDepth,32));check(reader->SetInput(stream.Get()));
    std::map<std::wstring,std::vector<std::wstring>> fields;
    auto known=[](const std::wstring& key){return key==L"Version"||key==L"BaseRenditionIsHDR"||key==L"GainMapMin"||key==L"GainMapMax"||key==L"Gamma"||key==L"OffsetSDR"||key==L"OffsetHDR"||key==L"HDRCapacityMin"||key==L"HDRCapacityMax";};
    auto trim=[](std::wstring text){auto first=text.find_first_not_of(L" \t\r\n"),last=text.find_last_not_of(L" \t\r\n");return first==std::wstring::npos?std::wstring():text.substr(first,last-first+1);};
    auto store=[&](const std::wstring& key,std::vector<std::wstring> values){if(values.empty()||values.size()>3||fields.contains(key))throw Failure{WINCODEC_ERR_BADIMAGE,L"HDR XMP 参数重复或数组长度无效"};for(auto& v:values){v=trim(v);if(v.empty()||v.size()>128)throw Failure{WINCODEC_ERR_BADIMAGE};}fields.emplace(key,std::move(values));};
    std::wstring field,text;std::vector<std::wstring> values;UINT fieldDepth=0,itemDepth=0;bool sequence=false,item=false;XmlNodeType type;HRESULT hr;unsigned nodes=0;
    while((hr=reader->Read(&type))==S_OK){
        if(++nodes>4096)throw Failure{WINCODEC_ERR_BADIMAGE};UINT depth;check(reader->GetDepth(&depth));
        if(type==XmlNodeType_Element){
            const wchar_t *name,*ns;check(reader->GetLocalName(&name,nullptr));check(reader->GetNamespaceUri(&ns,nullptr));bool empty=reader->IsEmptyElement()!=0;
            if(!field.empty()){
                if(!sequence&&depth==fieldDepth+1&&wcscmp(ns,rdfNs)==0&&wcscmp(name,L"Seq")==0){sequence=true;if(empty)throw Failure{WINCODEC_ERR_BADIMAGE};}
                else if(sequence&&!item&&depth==fieldDepth+2&&wcscmp(ns,rdfNs)==0&&wcscmp(name,L"li")==0){item=true;itemDepth=depth;text.clear();if(empty)throw Failure{WINCODEC_ERR_BADIMAGE};}
                else throw Failure{WINCODEC_ERR_BADIMAGE,L"HDR XMP 数组结构无效"};
            }else if(wcscmp(ns,hdrNs)==0&&known(name)){field=name;fieldDepth=depth;text.clear();values.clear();sequence=item=false;if(empty)throw Failure{WINCODEC_ERR_BADIMAGE};}
            if(reader->MoveToFirstAttribute()==S_OK){do{const wchar_t *key,*space,*value;check(reader->GetLocalName(&key,nullptr));check(reader->GetNamespaceUri(&space,nullptr));check(reader->GetValue(&value,nullptr));if(wcscmp(space,hdrNs)==0&&known(key))store(key,{value});}while(reader->MoveToNextAttribute()==S_OK);check(reader->MoveToElement());}
        }else if((type==XmlNodeType_Text||type==XmlNodeType_CDATA||type==XmlNodeType_Whitespace)&&!field.empty()&&(!sequence||item)){
            const wchar_t* value;UINT size;check(reader->GetValue(&value,&size));if(text.size()+size>128)throw Failure{WINCODEC_ERR_BADIMAGE};text.append(value,size);
        }else if(type==XmlNodeType_EndElement&&!field.empty()){
            // XmlLite reports an end tag at its contents' depth, one greater
            // than the matching start tag (unlike many DOM/pull readers).
            if(item&&depth==itemDepth+1){values.push_back(text);text.clear();item=false;if(values.size()>3)throw Failure{WINCODEC_ERR_BADIMAGE};}
            if(depth==fieldDepth+1){if(!sequence)values={text};store(field,std::move(values));field.clear();sequence=item=false;}
        }
    }
    check(hr);if(!field.empty())throw Failure{WINCODEC_ERR_BADIMAGE};
    auto version=fields.find(L"Version");if(version==fields.end()||version->second.size()!=1||version->second[0]!=L"1.0")throw Failure{WINCODEC_ERR_UNSUPPORTEDOPERATION,L"HDR XMP 版本尚不支持"};
    if(auto base=fields.find(L"BaseRenditionIsHDR");base!=fields.end()&&(base->second.size()!=1||base->second[0]!=L"False"))throw Failure{WINCODEC_ERR_UNSUPPORTEDOPERATION,L"HDR 基底的反向增益图尚不支持"};
    auto numeric=[&](const wchar_t* key,float fallback,bool required,bool scalar){std::array<float,3> result{fallback,fallback,fallback};auto found=fields.find(key);if(found==fields.end()){if(required)throw Failure{WINCODEC_ERR_BADIMAGE,L"HDR XMP 缺少必要参数"};return result;}auto& list=found->second;if((scalar&&list.size()!=1)||(!scalar&&list.size()!=1&&list.size()!=3))throw Failure{WINCODEC_ERR_BADIMAGE,L"HDR XMP 数组须为 1 或 3 个数值"};for(unsigned c=0;c<3;c++){auto& v=list[list.size()==1?0:c];wchar_t* end=nullptr;double number=wcstod(v.c_str(),&end);if(end==v.c_str()||*end||!std::isfinite(number)||std::abs(number)>FLT_MAX)throw Failure{WINCODEC_ERR_BADIMAGE};result[c]=float(number);}return result;};
    auto low=numeric(L"GainMapMin",0,false,false),high=numeric(L"GainMapMax",0,true,false),gamma=numeric(L"Gamma",1,false,false),sdr=numeric(L"OffsetSDR",1.0f/64,false,false),hdr=numeric(L"OffsetHDR",1.0f/64,false,false);
    auto minimum=numeric(L"HDRCapacityMin",0,false,true),maximum=numeric(L"HDRCapacityMax",0,true,true);
    uhdr_gainmap_metadata_t result{};result.hdr_capacity_min=std::exp2(minimum[0]);result.hdr_capacity_max=std::exp2(maximum[0]);result.use_base_cg=true;
    if(minimum[0]<0||maximum[0]<=minimum[0]||!std::isfinite(result.hdr_capacity_max))throw Failure{WINCODEC_ERR_BADIMAGE};
    for(unsigned c=0;c<3;c++){result.min_content_boost[c]=std::exp2(low[c]);result.max_content_boost[c]=std::exp2(high[c]);result.gamma[c]=gamma[c];result.offset_sdr[c]=sdr[c];result.offset_hdr[c]=hdr[c];if(high[c]<low[c]||result.min_content_boost[c]<=0||!std::isfinite(result.max_content_boost[c])||gamma[c]<=0||sdr[c]<0||hdr[c]<0)throw Failure{WINCODEC_ERR_BADIMAGE,L"HDR XMP 参数超出有效范围"};}
    return result;
}
