#!/bin/sh
# 正式環境部署（照其他專案的 deploy-main.sh）：
#   api → Docker image 與容器名稱都是 homo-bet-api，只對本機開 127.0.0.1:8101，由 nginx（/etc/nginx/conf.d/bet.conf）轉發 /api/
#   f2e → nginx 直接服務 f2e/dist，目前不在這支腳本裡 build
#
# 伺服器要先準備好（都不進版控）：
#   /var/project/bet/api/secrets.json
#   /var/project/bet/api/appsettings.json
set -e

PROJECT_DIR=/var/project/bet
NAME=homo-bet-api

cd $PROJECT_DIR
git checkout -f;
git fetch --all;
git pull origin main;

# 這兩個檔要先放好而且是「檔案」：不存在的話 docker run 會在主機上自動建一個同名資料夾，容器就掛載失敗起不來
for f in $PROJECT_DIR/api/secrets.json $PROJECT_DIR/api/appsettings.json; do
   if [ ! -f "$f" ]; then
      echo "找不到 $f（或它是資料夾），請先建好再部署。" >&2
      exit 1
   fi
done

cd ./api
# 先 build，失敗就停在這裡，舊的容器繼續跑
sudo docker build -t $NAME ./

if [ "$(sudo docker ps -q -f name=^$NAME$)" ]; then
   sudo docker stop $NAME
fi
if [ "$(sudo docker container ls -a -q -f name=^$NAME$)" ]; then
   sudo docker rm $NAME
fi

sudo docker run -d \
--log-opt max-size=10m --log-opt max-file=3 \
-e ASPNETCORE_URLS=http://\*:8080 -e ASPNETCORE_ENVIRONMENT=prod \
-e TZ=Asia/Taipei \
-p 127.0.0.1:8101:8080 \
--name $NAME \
--restart unless-stopped \
-v $PROJECT_DIR/api/secrets.json:/app/secrets.json \
-v $PROJECT_DIR/api/appsettings.json:/app/appsettings.json \
$NAME

git rev-parse HEAD
