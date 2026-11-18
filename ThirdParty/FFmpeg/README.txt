FFmpeg 64-bit static Windows build from www.gyan.dev

Version: 2026-11-16-git-d35e13b9e0-full_build-www.gyan.dev

License: GPL v3

Source Code: https://github.com/FFmpeg/FFmpeg/commit/d35e13b9e0

External Assets
frei0r plugins:   https://www.gyan.dev/ffmpeg/builds/ffmpeg-frei0r-plugins
lensfun database: https://www.gyan.dev/ffmpeg/builds/ffmpeg-lensfun-db
whisper models:   https://huggingface.co/ggerganov/whisper.cpp/tree/main

git-full build configuration: 

ARCH                      x86 (generic)
big-endian                no
runtime cpu detection     yes
standalone assembly       yes
x86 assembler             nasm
MMX enabled               yes
MMXEXT enabled            yes
SSE enabled               yes
SSSE3 enabled             yes
AESNI enabled             yes
CLMUL enabled             yes
AVX enabled               yes
AVX2 enabled              yes
AVX-512 enabled           yes
AVX-512ICL enabled        yes
AVX-VNNI enabled          yes
XOP enabled               yes
FMA3 enabled              yes
FMA4 enabled              yes
i686 features enabled     yes
CMOV is fast              yes
EBX available             yes
6 registers available     yes
7 registers available     yes
debug symbols             yes
strip symbols             yes
optimize for size         no
optimizations             yes
static                    yes
shared                    no
network support           yes
threading support         pthreads
safe bitstream reader     yes
texi2html enabled         no
perl enabled              yes
pod2man enabled           yes
makeinfo enabled          yes
makeinfo supports HTML    yes
experimental features     yes
xmllint enabled           yes

External libraries:
avisynth                libgsm                  libsvtav1
bzlib                   libharfbuzz             libsvtjpegxs
cairo                   libilbc                 libtheora
chromaprint             libjxl                  libtwolame
frei0r                  liblc3                  libuavs3d
gmp                     liblcevc_dec            libvidstab
gnutls                  liblensfun              libvmaf
iconv                   libmodplug              libvo_amrwbenc
ladspa                  libmp3lame              libvorbis
lcms2                   libmysofa               libvpx
libaom                  liboapv                 libvvenc
libaribb24              libopencolorio          libwebp
libaribcaption          libopencore_amrnb       libx264
libass                  libopencore_amrwb       libx265
libastcenc              libopenjpeg             libxavs2
libbluray               libopenmpt              libxevd
libbs2b                 libopus                 libxeve
libcaca                 libplacebo              libxml2
libcdio                 libqrencode             libxvid
libcodec2               libquirc                libzimg
libcurl                 librabbitmq             libzmq
libdav1d                librav1e                libzvbi
libdavs2                librist                 lzma
libdvdnav               librubberband           mediafoundation
libdvdread              libshine                openal
libflite                libsnappy               sdl2
libfontconfig           libsoxr                 vapoursynth
libfreetype             libspeex                whisper
libfribidi              libsrt                  zlib
libgme                  libssh

External libraries providing hardware acceleration:
amf                     d3d12va                 nvdec
cuda                    dxva2                   nvenc
cuda_llvm               ffnvcodec               opencl
cuvid                   libmfx                  vaapi
d3d11va                 libvpl                  vulkan

Libraries:
avcodec                 avformat                swscale
avdevice                avutil
avfilter                swresample

Programs:
ffmpeg                  ffplay                  ffprobe

Enabled decoders:
aac                     flv                     pcm_u16be
aac_fixed               fmvc                    pcm_u16le
aac_latm                fourxm                  pcm_u24be
aasc                    fraps                   pcm_u24le
ac3                     frwu                    pcm_u32be
ac3_fixed               ftr                     pcm_u32le
acelp_kelvin            g2m                     pcm_u8
adpcm_4xm               g723_1                  pcm_vidc
adpcm_adx               g728                    pcx
adpcm_afc               g729                    pdv
adpcm_agm               gdv                     pfm
adpcm_aica              gem                     pgm
adpcm_argo              gif                     pgmyuv
adpcm_circus            gremlin_dpcm            pgssub
adpcm_ct                gsm                     pgx
adpcm_dtk               gsm_ms                  phm
adpcm_ea                h261                    photocd
adpcm_ea_maxis_xa       h263                    pictor
adpcm_ea_r1             h263i                   pixlet
adpcm_ea_r2             h263p                   pjs
adpcm_ea_r3             h264                    png
adpcm_ea_xas            h264_amf                ppm
adpcm_g722              h264_cuvid              prores
adpcm_g726              h264_qsv                prores_raw
adpcm_g726le            hap                     prosumer
adpcm_ima_acorn         hca                     psd
adpcm_ima_alp           hcom                    ptx
adpcm_ima_amv           hdr                     qcelp
adpcm_ima_apc           hevc                    qdm2
adpcm_ima_apm           hevc_amf                qdmc
adpcm_ima_citrix        hevc_cuvid              qdraw
adpcm_ima_cunning       hevc_qsv                qoa
adpcm_ima_dat4          hnm4_video              qoi
adpcm_ima_dk3           hq_hqa                  qpeg
adpcm_ima_dk4           hqx                     qtrle
adpcm_ima_ea_eacs       huffyuv                 r10k
adpcm_ima_ea_sead       hymt                    r210
adpcm_ima_escape        iac                     ra_144
adpcm_ima_hvqm2         idcin                   ra_288
adpcm_ima_hvqm4         idf                     ralf
adpcm_ima_iss           iff_ilbm                rasc
adpcm_ima_magix         ilbc                    rawvideo
adpcm_ima_moflex        imc                     realtext
adpcm_ima_mtf           imm4                    rka
adpcm_ima_oki           imm5                    rl2
adpcm_ima_pda           imm6                    roq
adpcm_ima_qt            indeo2                  roq_dpcm
adpcm_ima_rad           indeo3                  rpza
adpcm_ima_smjpeg        indeo4                  rscc
adpcm_ima_ssi           indeo5                  rtv1
adpcm_ima_wav           interplay_acm           rv10
adpcm_ima_ws            interplay_dpcm          rv20
adpcm_ima_xbox          interplay_video         rv30
adpcm_ms                ipu                     rv40
adpcm_mtaf              jacosub                 rv60
adpcm_n64               jpeg2000                rw2
adpcm_psx               jpegls                  s302m
adpcm_psxc              jv                      sami
adpcm_rhetorex          kgv1                    sanm
adpcm_sanyo             kmvc                    sbc
adpcm_sbpro_2           lagarith                scpr
adpcm_sbpro_3           lead                    screenpresso
adpcm_sbpro_4           libaom_av1              sdx2_dpcm
adpcm_swf               libaribb24              sga
adpcm_thp               libaribcaption          sgi
adpcm_thp_le            libastcenc              sgirle
adpcm_vima              libcodec2               sheervideo
adpcm_xa                libdav1d                shorten
adpcm_xmd               libdavs2                simbiosis_imx
adpcm_yamaha            libgsm                  sipr
adpcm_zork              libgsm_ms               siren
agm                     libilbc                 smackaud
ahx                     libjxl                  smacker
aic                     libjxl_anim             smc
alac                    liblc3                  smvjpeg
alias_pix               libopencore_amrnb       snow
als                     libopencore_amrwb       sol_dpcm
amrnb                   libopus                 sp5x
amrwb                   libspeex                speedhq
amv                     libsvtjpegxs            speex
anm                     libuavs3d               srgc
ansi                    libvorbis               srt
anull                   libvpx_vp8              ssa
apac                    libvpx_vp9              stl
ape                     libxevd                 subrip
apng                    libzvbi_teletext        subviewer
aptx                    loco                    subviewer1
aptx_hd                 lscr                    sunrast
apv                     m101                    svq1
arbc                    mace3                   svq3
argo                    mace6                   tak
ass                     magicyuv                targa
asv1                    mdec                    targa_y216
asv2                    media100                tdsc
atrac1                  metasound               text
atrac3                  microdvd                theora
atrac3al                mimic                   thp
atrac3p                 misc4                   tiertexseqvideo
atrac3pal               mjpeg                   tiff
atrac9                  mjpeg_cuvid             tmv
aura                    mjpeg_qsv               truehd
aura2                   mjpegb                  truemotion1
av1                     mlp                     truemotion2
av1_amf                 mmvideo                 truemotion2rt
av1_cuvid               mobiclip                truespeech
av1_qsv                 motionpixels            tscc
avrn                    movtext                 tscc2
avrp                    mp1                     tta
avs                     mp1float                twinvq
avui                    mp2                     txd
bethsoftvid             mp2float                ulti
bfi                     mp3                     utvideo
bink                    mp3adu                  v210
binkaudio_dct           mp3adufloat             v210x
binkaudio_rdft          mp3float                vb
bintext                 mp3on4                  vble
bitpacked               mp3on4float             vbn
bmp                     mpc7                    vc1
bmv_audio               mpc8                    vc1_cuvid
bmv_video               mpeg1_cuvid             vc1_qsv
bonk                    mpeg1video              vc1image
brender_pix             mpeg2_cuvid             vcr1
c93                     mpeg2_qsv               vmdaudio
cavs                    mpeg2video              vmdvideo
cbd2_dpcm               mpeg4                   vmix
ccaption                mpeg4_cuvid             vmnc
cdgraphics              mpegvideo               vnull
cdtoons                 mpl2                    vorbis
cdxl                    msa1                    vp3
cfhd                    mscc                    vp4
cinepak                 msmpeg4v1               vp5
clearvideo              msmpeg4v2               vp6
cljr                    msmpeg4v3               vp6a
cllc                    msnsiren                vp6f
comfortnoise            msp2                    vp7
cook                    msrle                   vp8
cpia                    mss1                    vp8_cuvid
cri                     mss2                    vp8_qsv
cscd                    msvideo1                vp9
cyuv                    mszh                    vp9_amf
dca                     mts2                    vp9_cuvid
dds                     mv30                    vp9_qsv
derf_dpcm               mvc1                    vplayer
dfa                     mvc2                    vqa
dfpwm                   mvdv                    vqc
dirac                   mvha                    vvc
dnxhd                   mwsc                    vvc_qsv
dolby_e                 mxpeg                   wady_dpcm
dpx                     nellymoser              wavarc
dsd_lsbf                notchlc                 wavpack
dsd_lsbf_planar         nuv                     wbmp
dsd_msbf                on2avc                  wcmv
dsd_msbf_planar         opus                    webp
dsicinaudio             osq                     webp_anim
dsicinvideo             paf_audio               webvtt
dss_sp                  paf_video               wmalossless
dst                     pam                     wmapro
dvaudio                 pbm                     wmav1
dvbsub                  pcm_alaw                wmav2
dvdsub                  pcm_bluray              wmavoice
dvvideo                 pcm_dvd                 wmv1
dxa                     pcm_dvda                wmv2
dxtory                  pcm_f16le               wmv3
dxv                     pcm_f24le               wmv3image
eac3                    pcm_f32be               wnv1
eacmv                   pcm_f32le               wrapped_avframe
eamad                   pcm_f64be               ws_snd1
eatgq                   pcm_f64le               xan_dpcm
eatgv                   pcm_lxf                 xan_wc3
eatqi                   pcm_mulaw               xan_wc4
eightbps                pcm_pamf                xbin
eightsvx_exp            pcm_s16be               xbm
eightsvx_fib            pcm_s16be_planar        xface
escape124               pcm_s16le               xl
escape130               pcm_s16le_planar        xma1
evrc                    pcm_s24be               xma2
exr                     pcm_s24daud             xpm
fastaudio               pcm_s24le               xsub
ffv1                    pcm_s24le_planar        xwd
ffvhuff                 pcm_s32be               y41p
ffwavesynth             pcm_s32le               ylc
fic                     pcm_s32le_planar        yop
fits                    pcm_s64be               yuv4
flac                    pcm_s64le               zero12v
flashsv                 pcm_s8                  zerocodec
flashsv2                pcm_s8_planar           zlib
flic                    pcm_sga                 zmbv

Enabled encoders:
a64multi                hevc_d3d12va            pcm_s32be
a64multi5               hevc_mf                 pcm_s32le
aac                     hevc_nvenc              pcm_s32le_planar
aac_mf                  hevc_qsv                pcm_s64be
ac3                     hevc_vaapi              pcm_s64le
ac3_fixed               hevc_vulkan             pcm_s8
ac3_mf                  huffyuv                 pcm_s8_planar
adpcm_adx               jpeg2000                pcm_u16be
adpcm_argo              jpegls                  pcm_u16le
adpcm_g722              libaom_av1              pcm_u24be
adpcm_g726              libastcenc              pcm_u24le
adpcm_g726le            libcodec2               pcm_u32be
adpcm_ima_alp           libgsm                  pcm_u32le
adpcm_ima_amv           libgsm_ms               pcm_u8
adpcm_ima_apm           libilbc                 pcm_vidc
adpcm_ima_qt            libjxl                  pcx
adpcm_ima_ssi           libjxl_anim             pdv
adpcm_ima_wav           liblc3                  pfm
adpcm_ima_ws            libmp3lame              pgm
adpcm_ms                liboapv                 pgmyuv
adpcm_swf               libopencore_amrnb       phm
adpcm_yamaha            libopenjpeg             png
alac                    libopus                 ppm
alias_pix               librav1e                prores
amv                     libshine                prores_aw
anull                   libspeex                prores_ks
apng                    libsvtav1               prores_ks_vulkan
aptx                    libsvtjpegxs            qoi
aptx_hd                 libtheora               qtrle
apv_vulkan              libtwolame              r10k
ass                     libvo_amrwbenc          r210
asv1                    libvorbis               ra_144
asv2                    libvpx_vp8              rawvideo
av1_amf                 libvpx_vp9              roq
av1_d3d12va             libvvenc                roq_dpcm
av1_mf                  libwebp                 rpza
av1_nvenc               libwebp_anim            rv10
av1_qsv                 libx264                 rv20
av1_vaapi               libx264rgb              s302m
av1_vulkan              libx265                 sbc
avrp                    libxavs2                sgi
avui                    libxeve                 smc
bitpacked               libxvid                 snow
bmp                     ljpeg                   speedhq
cfhd                    magicyuv                srt
cinepak                 mjpeg                   ssa
cljr                    mjpeg_qsv               subrip
comfortnoise            mjpeg_vaapi             sunrast
dca                     mlp                     svq1
dfpwm                   movtext                 targa
dnxhd                   mp2                     text
dpx                     mp2fixed                tiff
dsd_msbf                mp3_mf                  truehd
dvbsub                  mpeg1video              tta
dvdsub                  mpeg2_qsv               ttml
dvvideo                 mpeg2_vaapi             utvideo
dxv                     mpeg2video              v210
eac3                    mpeg4                   vbn
exr                     msmpeg4v2               vc2
ffv1                    msmpeg4v3               vnull
ffv1_vulkan             msrle                   vorbis
ffvhuff                 msvideo1                vp8_vaapi
fits                    nellymoser              vp9_qsv
flac                    opus                    vp9_vaapi
flashsv                 pam                     wavpack
flashsv2                pbm                     wbmp
flv                     pcm_alaw                webvtt
g723_1                  pcm_bluray              wmav1
gif                     pcm_dvd                 wmav2
h261                    pcm_f32be               wmv1
h263                    pcm_f32le               wmv2
h263p                   pcm_f64be               wrapped_avframe
h264_amf                pcm_f64le               xbm
h264_d3d12va            pcm_mulaw               xface
h264_mf                 pcm_s16be               xsub
h264_nvenc              pcm_s16be_planar        xwd
h264_qsv                pcm_s16le               y41p
h264_vaapi              pcm_s16le_planar        yuv4
h264_vulkan             pcm_s24be               zlib
hap                     pcm_s24daud             zmbv
hdr                     pcm_s24le
hevc_amf                pcm_s24le_planar

Enabled hwaccels:
apv_vulkan              hevc_nvdec              vc1_nvdec
av1_d3d11va             hevc_nvdec_cuarray      vc1_nvdec_cuarray
av1_d3d11va2            hevc_vaapi              vc1_vaapi
av1_d3d12va             hevc_vulkan             vp8_nvdec
av1_dxva2               mjpeg_nvdec             vp8_nvdec_cuarray
av1_nvdec               mjpeg_vaapi             vp8_vaapi
av1_nvdec_cuarray       mpeg1_nvdec             vp9_d3d11va
av1_vaapi               mpeg1_nvdec_cuarray     vp9_d3d11va2
av1_vulkan              mpeg2_d3d11va           vp9_d3d12va
dpx_vulkan              mpeg2_d3d11va2          vp9_dxva2
ffv1_vulkan             mpeg2_d3d12va           vp9_nvdec
h263_vaapi              mpeg2_dxva2             vp9_nvdec_cuarray
h264_d3d11va            mpeg2_nvdec             vp9_vaapi
h264_d3d11va2           mpeg2_nvdec_cuarray     vp9_vulkan
h264_d3d12va            mpeg2_vaapi             vvc_vaapi
h264_dxva2              mpeg4_nvdec             wmv3_d3d11va
h264_nvdec              mpeg4_nvdec_cuarray     wmv3_d3d11va2
h264_nvdec_cuarray      mpeg4_vaapi             wmv3_d3d12va
h264_vaapi              prores_raw_vulkan       wmv3_dxva2
h264_vulkan             prores_vulkan           wmv3_nvdec
hevc_d3d11va            vc1_d3d11va             wmv3_nvdec_cuarray
hevc_d3d11va2           vc1_d3d11va2            wmv3_vaapi
hevc_d3d12va            vc1_d3d12va
hevc_dxva2              vc1_dxva2

Enabled parsers:
aac                     dvdsub                  mpegaudio
aac_latm                evc                     mpegvideo
ac3                     ffv1                    opus
adx                     flac                    png
ahx                     ftr                     pnm
amr                     g723_1                  prores
apv                     g729                    prores_raw
av1                     gif                     qoi
avs2                    gsm                     rv34
avs3                    h261                    rw2
bmp                     h263                    sbc
cavsvideo               h264                    sipr
cook                    hdr                     tak
cri                     hevc                    vc1
dca                     ipu                     vorbis
dirac                   jpeg2000                vp3
dnxhd                   jpegxl                  vp8
dnxuc                   jpegxs                  vp9
dolby_e                 lcevc                   vvc
dpx                     misc4                   webp
dvaudio                 mjpeg                   xbm
dvbsub                  mlp                     xma
dvd_nav                 mpeg4video              xwd

Enabled demuxers:
aa                      idf                     pcm_f32le
aac                     iff                     pcm_f64be
aax                     ifv                     pcm_f64le
ac3                     ilbc                    pcm_mulaw
ac4                     image2                  pcm_s16be
ace                     image2_alias_pix        pcm_s16le
acm                     image2_brender_pix      pcm_s24be
act                     image2pipe              pcm_s24le
adf                     image_bmp_pipe          pcm_s32be
adp                     image_cri_pipe          pcm_s32le
ads                     image_dds_pipe          pcm_s8
adx                     image_dpx_pipe          pcm_u16be
aea                     image_exr_pipe          pcm_u16le
afc                     image_gem_pipe          pcm_u24be
aiff                    image_gif_pipe          pcm_u24le
aix                     image_hdr_pipe          pcm_u32be
alp                     image_j2k_pipe          pcm_u32le
amr                     image_jpeg_pipe         pcm_u8
amrnb                   image_jpegls_pipe       pcm_vidc
amrwb                   image_jpegxl_pipe       pdv
anm                     image_jpegxs_pipe       pjs
apac                    image_pam_pipe          pmp
apc                     image_pbm_pipe          pp_bnk
ape                     image_pcx_pipe          pva
apm                     image_pfm_pipe          pvf
apng                    image_pgm_pipe          qcp
aptx                    image_pgmyuv_pipe       qoa
aptx_hd                 image_pgx_pipe          r3d
apv                     image_phm_pipe          rawvideo
aqtitle                 image_photocd_pipe      rcwt
argo_asf                image_pictor_pipe       realtext
argo_brp                image_png_pipe          redspark
argo_cvg                image_ppm_pipe          rka
asf                     image_psd_pipe          rl2
ass                     image_qdraw_pipe        rm
ast                     image_qoi_pipe          roq
astc                    image_rw2_pipe          rpl
au                      image_sgi_pipe          rsd
av1                     image_sunrast_pipe      rso
avi                     image_svg_pipe          rtp
avisynth                image_tiff_pipe         rtsp
avr                     image_vbn_pipe          s337m
avs                     image_webp_pipe         sami
avs2                    image_xbm_pipe          sap
avs3                    image_xpm_pipe          sbc
bethsoftvid             image_xwd_pipe          sbg
bfi                     imf                     scc
bfstm                   ingenient               scd
bink                    ipmovie                 sdns
binka                   ipu                     sdp
bintext                 ircam                   sdr2
bit                     iss                     sds
bitpacked               iv8                     sdx
bmv                     ivf                     segafilm
boa                     ivr                     ser
bonk                    jacosub                 sga
brstm                   jpeg_mpf                shorten
c93                     jpegxl_anim             siff
caf                     jv                      simbiosis_imx
cavsvideo               ktx                     sln
cdg                     kux                     smacker
cdxl                    kvag                    smjpeg
cine                    laf                     smush
codec2                  lc3                     sol
codec2raw               lcevc                   sox
concat                  libgme                  spdif
dash                    libmodplug              srt
data                    libopenmpt              stl
daud                    live_flv                str
dcstr                   lmlm4                   subviewer
derf                    loas                    subviewer1
dfa                     lrc                     sup
dfpwm                   luodat                  svag
dhav                    lvf                     svs
dirac                   lxf                     swf
dnxhd                   m4v                     tak
dsf                     matroska                tedcaptions
dsicin                  mca                     thp
dss                     mcc                     threedostr
dts                     mgsts                   tiertexseq
dtshd                   microdvd                tmv
dv                      mjpeg                   truehd
dvbsub                  mjpeg_2000              tta
dvbtxt                  mlp                     tty
dvdvideo                mlv                     txd
dxa                     mm                      ty
ea                      mmf                     usm
ea_cdata                mods                    v210
eac3                    moflex                  v210x
epaf                    mov                     vag
evc                     mp3                     vapoursynth
ffmetadata              mpc                     vc1
filmstrip               mpc8                    vc1t
fits                    mpegps                  vividas
flac                    mpegts                  vivo
flic                    mpegtsraw               vmd
flv                     mpegvideo               vobsub
fourxm                  mpjpeg                  voc
frm                     mpl2                    vpk
fsb                     mpsub                   vplayer
fwse                    msf                     vqf
g722                    msnwc_tcp               vvc
g723_1                  msp                     w64
g726                    mtaf                    wady
g726le                  mtv                     wav
g728                    musx                    wavarc
g729                    mv                      wc3
gdv                     mvi                     webm_dash_manifest
genh                    mvr                     webp_anim
gif                     mxf                     webvtt
gsm                     mxg                     wsaud
gxf                     nc                      wsd
h261                    nistsphere              wsvqa
h263                    nsp                     wtv
h264                    nsv                     wv
hca                     nut                     wve
hcom                    nuv                     xa
hevc                    obu                     xbin
hls                     ogg                     xmd
hnm                     oma                     xmv
hxvs                    osq                     xvag
iamf                    paf                     xwma
ico                     pcm_alaw                yop
idcin                   pcm_f32be               yuv4mpegpipe

Enabled muxers:
a64                     h264                    pcm_s16le
ac3                     hash                    pcm_s24be
ac4                     hds                     pcm_s24le
adts                    hevc                    pcm_s32be
adx                     hls                     pcm_s32le
aea                     iamf                    pcm_s8
aiff                    ico                     pcm_u16be
alp                     ilbc                    pcm_u16le
amr                     image2                  pcm_u24be
amv                     image2pipe              pcm_u24le
apm                     ipod                    pcm_u32be
apng                    ircam                   pcm_u32le
aptx                    ismv                    pcm_u8
aptx_hd                 iterm2                  pcm_vidc
apv                     ivf                     pdv
argo_asf                jacosub                 psp
argo_cvg                jpeg_mpf                rawvideo
asf                     ktx                     rcwt
asf_stream              kvag                    rm
ass                     latm                    roq
ast                     lc3                     rso
astc                    lcevc                   rtp
au                      lrc                     rtp_mpegts
avi                     m4v                     rtsp
avif                    matroska                sap
avm2                    matroska_audio          sbc
avs2                    mcc                     scc
avs3                    md5                     segafilm
bit                     microdvd                segment
caf                     mjpeg                   smjpeg
cavsvideo               mkvtimestamp_v2         smoothstreaming
chromaprint             mlp                     sox
codec2                  mmf                     spdif
codec2raw               mov                     spx
crc                     mp2                     srt
dash                    mp3                     stream_segment
data                    mp4                     streamhash
daud                    mpeg1system             sup
dfpwm                   mpeg1vcd                swf
dirac                   mpeg1video              tee
dnxhd                   mpeg2dvd                tg2
dts                     mpeg2svcd               tgp
dv                      mpeg2video              truehd
eac3                    mpeg2vob                tta
evc                     mpegts                  ttml
f4v                     mpjpeg                  uncodedframecrc
ffmetadata              mxf                     vc1
fifo                    mxf_d10                 vc1t
filmstrip               mxf_opatom              voc
fits                    null                    vvc
flac                    nut                     w64
flv                     obu                     wav
framecrc                oga                     webm
framehash               ogg                     webm_chunk
framemd5                ogv                     webm_dash_manifest
g722                    oma                     webp
g723_1                  opus                    webvtt
g726                    pcm_alaw                whip
g726le                  pcm_f32be               wsaud
gif                     pcm_f32le               wtv
gsm                     pcm_f64be               wv
gxf                     pcm_f64le               yuv4mpegpipe
h261                    pcm_mulaw
h263                    pcm_s16be

Enabled protocols:
async                   httpproxy               rtmp
bluray                  https                   rtmpe
cache                   icecast                 rtmps
concat                  ipfs_gateway            rtmpt
concatf                 ipns_gateway            rtmpte
crypto                  libamqp                 rtmpts
data                    libcurl                 rtp
dtls                    librist                 shared
fd                      libsrt                  srtp
ffrtmpcrypt             libssh                  subfile
ffrtmphttp              libzmq                  tcp
file                    md5                     tee
ftp                     mmsh                    tls
gopher                  mmst                    udp
gophers                 pipe                    udplite
http                    prompeg                 windows_handle

Enabled filters:
a3dscope                deesser                 pan
aap                     deflate                 perlin
abench                  deflicker               perms
abitscope               deinterlace_d3d12       perspective
acompressor             deinterlace_qsv         phase
acontrast               deinterlace_vaapi       photosensitivity
acopy                   dejudder                pixdesctest
acrossfade              delogo                  pixelize
acrossover              denoise_vaapi           pixscope
acrusher                deshake                 pp7
acue                    deshake_opencl          premultiply
addroi                  despill                 premultiply_dynamic
adeclick                detelecine              prewitt
adeclip                 dialoguenhance          prewitt_opencl
adecorrelate            dilation                procamp_vaapi
adelay                  dilation_opencl         program_opencl
adenorm                 displace                pseudocolor
aderivative             doubleweave             psnr
adrawgraph              drawbox                 pullup
adrc                    drawbox_vaapi           qp
adynamicequalizer       drawgraph               qrencode
adynamicsmooth          drawgrid                qrencodesrc
aecho                   drawtext                quirc
aemphasis               drawvg                  random
aeval                   drmeter                 readeia608
aevalsrc                dynaudnorm              readvitc
aexciter                earwax                  realtime
afade                   ebur128                 remap
afdelaysrc              edgedetect              remap_opencl
afftdn                  elbg                    removegrain
afftfilt                entropy                 removelogo
afir                    epx                     repeatfields
afireqsrc               eq                      replaygain
afirsrc                 equalizer               reverse
aformat                 erosion                 rgbashift
afreqshift              erosion_opencl          rgbtestsrc
afwtdn                  estdif                  roberts
agate                   exposure                roberts_opencl
agraphmonitor           extractplanes           rotate
ahistogram              extrastereo             rubberband
aiir                    fade                    sab
aintegral               feedback                scale
ainterleave             fftdnoiz                scale2ref
alatency                fftfilt                 scale_cuda
alimiter                field                   scale_d3d11
allpass                 fieldhint               scale_d3d12
allrgb                  fieldmatch              scale_qsv
allyuv                  fieldorder              scale_vaapi
aloop                   fillborders             scale_vulkan
alphaextract            find_rect               scdet
alphamerge              firequalizer            scdet_vulkan
amerge                  flanger                 scharr
ametadata               flip_vulkan             scroll
amf_capture             flite                   segment
amix                    floodfill               select
amovie                  format                  selectivecolor
amplify                 fps                     sendcmd
amultiply               framepack               separatefields
anequalizer             framerate               setdar
anlmdn                  framestep               setfield
anlmf                   frc_amf                 setparams
anlms                   freezedetect            setpts
anoisesrc               freezeframes            setrange
anull                   frei0r                  setsar
anullsink               frei0r_src              settb
anullsrc                fruc_vulkan             sharpness_vaapi
apad                    fspp                    shear
aperms                  fsync                   showcqt
aphasemeter             gainmap                 showcwt
aphaser                 gblur                   showfreqs
aphaseshift             gblur_vulkan            showinfo
apsnr                   geq                     showpalette
apsyclip                gfxcapture              showspatial
apulsator               gradfun                 showspectrum
arealtime               gradients               showspectrumpic
aresample               graphmonitor            showvolume
areverse                grayworld               showwaves
arls                    greyedge                showwavespic
arnndn                  guided                  shuffleframes
asdr                    haas                    shufflepixels
asegment                haldclut                shuffleplanes
aselect                 haldclutsrc             sidechaincompress
asendcmd                hdcd                    sidechaingate
asetnsamples            headphone               sidedata
asetpts                 hflip                   sierpinski
asetrate                hflip_vulkan            signalstats
asettb                  highpass                signature
ashowinfo               highshelf               silencedetect
asidedata               hilbert                 silenceremove
asisdr                  histeq                  sinc
asoftclip               histogram               sine
aspectralstats          hqdn3d                  siti
asplit                  hqx                     smartblur
ass                     hstack                  smptebars
astats                  hstack_qsv              smptehdbars
astreamselect           hstack_vaapi            sobel
asubboost               hsvhold                 sobel_opencl
asubcut                 hsvkey                  sofalizer
asupercut               hue                     spectrumsynth
asuperpass              huesaturation           speechnorm
asuperstop              hwdownload              split
atadenoise              hwmap                   spp
atempo                  hwupload                sr_amf
atilt                   hwupload_cuda           ssim
atrim                   hysteresis              ssim360
avectorscope            iccdetect               stereo3d
avgblur                 iccgen                  stereotools
avgblur_opencl          identity                stereowiden
avgblur_vulkan          idet                    streamselect
avsynctest              il                      subtitles
axcorrelate             inflate                 super2xsai
azmq                    interlace               superequalizer
backgroundkey           interlace_vulkan        surround
bandpass                interleave              swaprect
bandreject              join                    swapuv
bass                    kerndeint               tblend
bbox                    kirsch                  telecine
bench                   ladspa                  testsrc
bilateral               lagfun                  testsrc2
bilateral_cuda          latency                 thistogram
biquad                  latticepal              threshold
bitplanenoise           lcevc                   thumbnail
blackdetect             lenscorrection          thumbnail_cuda
blackdetect_vulkan      lensfun                 tile
blackframe              libplacebo              tiltandshift
blend                   libplacebo_scene        tiltshelf
blend_vulkan            libvmaf                 tinterlace
blockdetect             life                    tlut2
blurdetect              limitdiff               tmedian
bm3d                    limiter                 tmidequalizer
boxblur                 loop                    tmix
boxblur_opencl          loudnorm                tonemap
bs2b                    lowpass                 tonemap_opencl
bwdif                   lowshelf                tonemap_vaapi
bwdif_cuda              lumakey                 tpad
bwdif_vulkan            lut                     transpose
cas                     lut1d                   transpose_cuda
ccrepack                lut2                    transpose_opencl
cellauto                lut3d                   transpose_vaapi
channelmap              lutrgb                  transpose_vulkan
channelsplit            lutyuv                  treble
chorus                  mandelbrot              tremolo
chromaber_vulkan        maskedclamp             trim
chromahold              maskedmax               unpremultiply
chromakey               maskedmerge             unsharp
chromakey_cuda          maskedmin               unsharp_opencl
chromanr                maskedthreshold         untile
chromashift             maskfun                 uspp
ciescope                mcdeint                 v360
codecview               mcompand                v360_vulkan
color                   median                  vaguedenoiser
color_vulkan            mergeplanes             varblur
colorbalance            mestimate               vectorscope
colorchannelmixer       mestimate_d3d12         vflip
colorchart              metadata                vflip_vulkan
colorcontrast           midequalizer            vfrdet
colorcorrect            minterpolate            vibrance
colordetect             mix                     vibrato
colorhold               monochrome              vidstabdetect
colorize                morpho                  vidstabtransform
colorkey                movie                   vif
colorkey_opencl         mpdecimate              vignette
colorlevels             mptestsrc               virtualbass
colormap                msad                    vmafmotion
colormatrix             multiply                volume
colorspace              negate                  volumedetect
colorspace_cuda         nlmeans                 vpp_amf
colorspectrum           nlmeans_opencl          vpp_qsv
colortemperature        nlmeans_vulkan          vqe_amf
compand                 nnedi                   vstack
compensationdelay       noformat                vstack_qsv
concat                  noise                   vstack_vaapi
convolution             normalize               w3fdif
convolution_opencl      null                    waveform
convolve                nullsink                weave
copy                    nullsrc                 whisper
corr                    ocio                    xbr
cover_rect              openclsrc               xcorrelate
crop                    oscilloscope            xfade
cropdetect              overlay                 xfade_opencl
crossfeed               overlay_cuda            xfade_vulkan
crystalizer             overlay_opencl          xmedian
cue                     overlay_qsv             xpsnr
curves                  overlay_vaapi           xstack
datascope               overlay_vulkan          xstack_qsv
dblur                   owdenoise               xstack_vaapi
dcshift                 pad                     yadif
dctdnoiz                pad_cuda                yadif_cuda
ddagrab                 pad_opencl              yaepblur
deband                  pad_vaapi               yuvtestsrc
deblock                 pal100bars              zmq
decimate                pal75bars               zoneplate
deconvolve              palettegen              zoompan
dedot                   paletteuse              zscale

Enabled bsfs:
aac_adtstoasc           h264_mp4toannexb        pgs_frame_merge
ahx_to_mp2              h264_redundant_pps      prores_metadata
apv_metadata            hapqa_extract           remove_extradata
av1_frame_merge         hevc_metadata           setts
av1_frame_split         hevc_mp4toannexb        showinfo
av1_metadata            imx_dump_header         side_data
chomp                   jpegapp                 smpte436m_to_eia608
dca_core                lcevc_merge             text2movsub
dovi_rpu                lcevc_metadata          trace_headers
dovi_split              media100_to_mjpegb      trim
dts2pts                 mjpeg2jpeg              truehd_core
dump_extradata          mjpega_dump_header      vp9_metadata
dv_error_marker         mov2textsub             vp9_raw_reorder
eac3_core               mpeg2_metadata          vp9_superframe
eia608_to_smpte436m     mpeg4_unpack_bframes    vp9_superframe_split
evc_frame_merge         noise                   vvc_metadata
extract_extradata       null                    vvc_mp4toannexb
filter_units            opus_metadata
h264_metadata           pcm_rechunk

Enabled indevs:
dshow                   lavfi                   openal
gdigrab                 libcdio                 vfwcap

Enabled outdevs:
caca

git-full external libraries' versions: 

amf-headers         1.5.3-1
aom                 v3.15.3-196-ge0425ecc02
aribb24             v1.0.3-5-g5e9be27
astc-encoder        5.7.1-115-gdba7cfd
avisynth+           v3.7.6-82-gb5ec8855
cairo               1.18.6-9-g07adef44b
chromaprint         1.6.2-1
codec2              1.2.0-108-g310777b1
curl                curl-8_23_0-379-g9637d6b844
dav1d               1.5.4-58-g39834d7f
davs2               1.7-1-gb41cf11
dvdnav              7.0.0-18-g8147ccd
dvdread             7.1.1-125-g0f30df5
ffnvcodec           n13.1.15.0-4-gf205307
flite               v2.2-55-g6c9f20d
freetype            VER-2-15-0-30-gdbb00e52
frei0r              v1.5-497-g6156f2e
fribidi             v1.0.18
game-music-emu      0.6.3-250-g8238afd
gsm                 1.0.24-1
harfbuzz            15.1.0-143-g7621b1cf
ladspa-sdk          1.17-2
lame                4.0-1
lc3                 v1.1.3-11-gf9c127f
lcms2               2.20-1
lensfun             v0.3.2-3456-g52cf1a7b
libaribcaption      v1.0.0-121-g517b011
libass              0.17.5-25-g21b5a54
libbluray           1.5.1-6-g1398cc9
libbs2b             3.1.0
libcaca             0.99.beta20-8
libcdio-paranoia    10.2+2.0.2-2
libilbc             v3.0.4-346-g6adb26d4a4
libjxl              v0.13-snapshot-1-g0c65c190
liblcevc-dec        4.3.1
libmodplug          0.8.9.0-5
libmysofa           v1.3.1-79-g648eed0
libopencolorio      v1.0.8-961-g22dd9c4f
libplacebo          v7.387.0-66-g58dfee7
librabbitmq         v0.18.1-1-g540e497
librist             v0.2.20-18-ga911bee
libsoxr             0.1.3
libssh              0.12.2-1
libvo-amrwbenc      v0.1.3-7-g884dd24
libvpl              v2.17.0-2-g412c5cf
libwebp             v1.6.0-398-g7c382bd
openal-soft         255a0e2785
openapv             v1.1.3.0-3-g3f02913
opencl-headers      v2026.05.29-11-g82f6031
opencl-loader       v2026.05.29-9-gd0be526
opencore-amr        0.1.6-1
openjpeg2           2.5.4-2
openmpt             libopenmpt-0.2.6774-beta20-11233-g8ec17a9c
opus                v1.6.1-101-g0145a624
qrencode            v4.0.0-rc3-102-g715e29f
quirc               v1.2-14-gf2e4271
rav1e               p20250624-8-g31435de
rubberband          v4.0.0-2-ge4296ac
SDL                 release-3.4.0-1837-gfda4ece27
shine               3.1.1-59-gab5e352
snappy              1.3.1-1
speex               Speex-1.2.1-58-g09b569d
srt                 v1.5.7-25-g80e0bc4
SVT-AV1             v4.3.0-139-gd0d5c7e01
SVT-JPEG-XS         v0.10.0-31-g2dc3564
theora              v1.2.0alpha1-224-g28fd5ec
twolame             0.4.0-3
uavs3d              1.2
vaapi               2.11.0.pre1-281-gf052e984
vapoursynth         R82-2-gdfe97e09
vidstab             v1.1.0-291-ge2445c4
vmaf                v3.2.1-71-ga6ed0f1
vorbis              v1.3.7-37-g1b75110b
vpx                 v1.17.0-100-ge019cffb8
vulkan-loader       sdk-0.1.0-4138-g0b137080
vvenc               v1.14.0-471-ga455d51
whisper             v1.9.5-334-g5922450f
x264                0.165.3223
x265                4.3-107-g0aa41217
xavs2               1.4
xevd                v0.8.0
xeve                v0.7.1
xvidcore            1.3.7-5
zeromq              4.3.5-5
zimg                release-3.0.6-253-g67e0603
zvbi                v0.2.46

